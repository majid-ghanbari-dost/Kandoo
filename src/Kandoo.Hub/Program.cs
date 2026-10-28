using System.Text.Json;
using Kandoo.Core;
using Kandoo.Hub;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
});
var sqliteConnection = builder.Configuration.GetConnectionString("HubSqlite")
    ?? "Data Source=kandoo-hub.db;Pooling=False";
var hubStore = new SqliteHubStore(sqliteConnection);
builder.Services.AddSingleton<IHubStore>(hubStore);
builder.Services.AddSingleton<IHubAccessStore>(hubStore);
builder.Services.AddSingleton<IClock, SystemHubClock>();
builder.Services.AddSingleton<HubOperationService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { status = "ok" }, HubJson.Options));

app.MapPost("/v1/stores/{store_id}/owner-sessions", (
    Guid store_id,
    OwnerSessionRequest? request,
    IConfiguration configuration,
    IHubAccessStore access,
    IClock clock) =>
{
    var configuredProof = configuration["Hub:OwnerBootstrapProof"];
    if (string.IsNullOrWhiteSpace(configuredProof))
    {
        return HubHttp.Error(503, "OWNER_BOOTSTRAP_UNAVAILABLE", "Owner bootstrap is not configured.");
    }

    if (request is null || request.HubDeviceId == Guid.Empty || request.OwnerUserId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.BootstrapProof) ||
        !HubToken.FixedTimeEquals(HubToken.Hash(configuredProof), HubToken.Hash(request.BootstrapProof)))
    {
        return HubHttp.Error(403, "OWNER_BOOTSTRAP_INVALID", "The owner bootstrap proof is invalid.");
    }

    var now = clock.UtcNow;
    var issued = access.IssueOwnerSession(
        store_id, request.HubDeviceId, request.OwnerUserId,
        Guid.NewGuid().ToString("D"), now, now.AddHours(1));
    return Results.Json(new
    {
        status = "SESSION_ACTIVE",
        session = SessionBody(issued.Session),
        session_token = issued.SessionToken
    }, HubJson.Options);
});

app.MapPost("/v1/stores/{store_id}/pairing-grants", (
    Guid store_id,
    PairingGrantRequest? request,
    HttpContext context,
    IHubAccessStore access,
    IClock clock) =>
{
    var auth = Authenticate(context, store_id, access, clock);
    if (!auth.IsAuthenticated)
    {
        return AuthError(auth.Code);
    }

    if (!IsOwner(auth))
    {
        return HubHttp.Error(403, "OWNER_REQUIRED", "An active owner session is required.");
    }

    if (request is null || request.HubDeviceId == Guid.Empty ||
        request.HubDeviceId != auth.Session!.DeviceId ||
        !string.Equals(request.RequestedDeviceRole, "COMPANION", StringComparison.Ordinal) ||
        !Uri.TryCreate(request.HubEndpoint, UriKind.Absolute, out var endpoint) ||
        endpoint.Scheme != Uri.UriSchemeHttps || endpoint.IsLoopback)
    {
        return HubHttp.Error(400, "PAIRING_INVALID", "A companion-only grant requires the current Hub and a LAN HTTPS endpoint.");
    }

    var now = clock.UtcNow;
    var nonce = HubToken.NewToken(16);
    var grant = access.CreatePairingGrant(
        store_id, request.HubDeviceId, auth.Session.OwnerUserId!.Value,
        request.HubEndpoint!, $"pair-{Guid.NewGuid():N}", HubToken.Hash(nonce), now, now.AddMinutes(5));
    return Results.Json(new
    {
        status = "PAIRING_QR_ISSUED",
        qr = new
        {
            protocol_version = "0",
            pairing_id = grant.PairingId,
            store_id = grant.StoreId,
            hub_device_id = grant.HubDeviceId,
            device_role = "COMPANION",
            hub_endpoint = grant.HubEndpoint,
            nonce,
            issued_at = grant.IssuedAt,
            expires_at = grant.ExpiresAt,
            single_use = true
        }
    }, HubJson.Options);
});

app.MapPost("/v1/pairing-grants/consume", (
    PairingConsumeRequest? request,
    IHubAccessStore access,
    IClock clock) =>
{
    if (request is null || request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.PairingId) ||
        string.IsNullOrWhiteSpace(request.Nonce) || string.IsNullOrWhiteSpace(request.Label) ||
        string.IsNullOrWhiteSpace(request.Platform) || string.IsNullOrWhiteSpace(request.DevicePublicKey))
    {
        return HubHttp.Error(400, "PAIRING_INVALID", "Pairing identity, proof, label, platform, and public key are required.");
    }

    var now = clock.UtcNow;
    var result = access.ConsumePairingGrant(
        request.PairingId, request.Nonce, request.DeviceId, request.Label,
        request.Platform, request.DevicePublicKey, HubToken.NewToken(),
        Guid.NewGuid().ToString("D"), now, now.AddHours(1));
    if (result.Code != "DEVICE_PAIRED")
    {
        return result.Code switch
        {
            "PAIRING_EXPIRED" => HubHttp.Error(410, result.Code, "The pairing grant has expired.", "REQUEST_NEW_GRANT"),
            "PAIRING_REPLAY_REJECTED" => HubHttp.Error(409, result.Code, "The pairing grant was already consumed."),
            "DEVICE_ALREADY_PAIRED" => HubHttp.Error(409, result.Code, "The device is already paired."),
            _ => HubHttp.Error(400, result.Code, "The pairing grant or proof is invalid.")
        };
    }

    return Results.Json(new
    {
        status = result.Code,
        device = DeviceBody(result.Device!),
        session = SessionBody(result.Session!.Session),
        session_token = result.Session.SessionToken,
        device_credential = result.DeviceCredential,
        qr_consumed = true,
        hub_role_granted = false
    }, HubJson.Options);
});

app.MapPost("/v1/stores/{store_id}/devices/{device_id}/sessions", (
    Guid store_id,
    Guid device_id,
    SessionResumeRequest? request,
    IHubAccessStore access,
    IClock clock) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.DeviceCredential))
    {
        return HubHttp.Error(400, "DEVICE_CREDENTIAL_INVALID", "A device credential is required.");
    }

    var now = clock.UtcNow;
    var result = access.ResumeSession(
        store_id, device_id, request.DeviceCredential, Guid.NewGuid().ToString("D"), now, now.AddHours(1));
    return result.Code == "SESSION_ACTIVE"
        ? Results.Json(new
        {
            status = result.Code,
            session = SessionBody(result.Session!.Session),
            session_token = result.Session.SessionToken,
            operation_id_namespace_unchanged = true
        }, HubJson.Options)
        : HubHttp.Error(result.Code == "DEVICE_REVOKED" ? 403 : 401, result.Code, "The device session cannot be resumed.");
});

app.MapGet("/v1/stores/{store_id}/devices", (
    Guid store_id,
    HttpContext context,
    IHubAccessStore access,
    IClock clock) =>
{
    var auth = Authenticate(context, store_id, access, clock);
    if (!auth.IsAuthenticated)
    {
        return AuthError(auth.Code);
    }

    return !IsOwner(auth)
        ? HubHttp.Error(403, "OWNER_REQUIRED", "An active owner session is required.")
        : Results.Json(access.GetDevices(store_id).Select(DeviceBody), HubJson.Options);
});

app.MapPost("/v1/stores/{store_id}/devices/{device_id}/revoke", (
    Guid store_id,
    Guid device_id,
    HttpContext context,
    IHubAccessStore access,
    IClock clock,
    HubOperationService operations) =>
{
    var auth = Authenticate(context, store_id, access, clock);
    if (!auth.IsAuthenticated)
    {
        return AuthError(auth.Code);
    }

    if (!IsOwner(auth))
    {
        return HubHttp.Error(403, "OWNER_REQUIRED", "An active owner session is required.");
    }

    return operations.Revoke(store_id, device_id, auth.Session!.OwnerUserId!.Value)
        ? Results.Json(new { status = "DEVICE_REVOKED" }, HubJson.Options)
        : HubHttp.Error(404, "DEVICE_NOT_PAIRED", "The companion device is not registered.");
});

app.MapPost("/v1/stores/{store_id}/devices/{device_id}/disconnect", (
    Guid store_id,
    Guid device_id,
    HttpContext context,
    IHubAccessStore access,
    IClock clock,
    HubOperationService operations) =>
{
    var auth = Authenticate(context, store_id, access, clock);
    if (!auth.IsAuthenticated)
    {
        return AuthError(auth.Code);
    }

    if (!IsOwner(auth) && auth.Session!.DeviceId != device_id)
    {
        return HubHttp.Error(403, "STORE_ACCESS_DENIED", "A companion can disconnect only its own session.");
    }

    var released = operations.Disconnect(store_id, device_id);
    return Results.Json(new { status = "DISCONNECTED", locks_released = released }, HubJson.Options);
});

app.MapGet("/v1/stores/{store_id}/draft-locks", (
    Guid store_id,
    string? customer_phone,
    HttpContext context,
    IHubAccessStore access,
    IClock clock,
    HubOperationService operations) =>
{
    var auth = Authenticate(context, store_id, access, clock);
    if (!auth.IsAuthenticated)
    {
        return AuthError(auth.Code);
    }

    if (string.IsNullOrWhiteSpace(customer_phone))
    {
        return HubHttp.Error(400, "VALIDATION_ERROR", "customer_phone is required.");
    }

    var current = operations.GetLock(store_id, customer_phone);
    return current is null
        ? HubHttp.Error(404, "LOCK_NOT_FOUND", "No lock exists for this store and phone.")
        : Results.Json(LockBody(current), HubJson.Options);
});

app.MapPost("/v1/stores/{store_id}/operations", async (
    Guid store_id,
    HttpContext context,
    IHubAccessStore access,
    IClock clock,
    HubOperationService operations) =>
{
    // Authenticate before parsing or validating the mutation so revoked devices
    // cannot produce domain effects even for a late or malformed write.
    var auth = Authenticate(context, store_id, access, clock);
    if (!auth.IsAuthenticated)
    {
        return AuthError(auth.Code);
    }

    HubOperationRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<HubOperationRequest>(context.Request.Body, HubJson.Options);
    }
    catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
    {
        request = null;
    }

    if (request is null)
    {
        return Results.Json(new HubOperationReply(
            "VALIDATION_ERROR", "REJECTED", null, null, false, HubJson.Element(new { })),
            HubJson.Options, statusCode: 400);
    }

    var result = operations.Submit(store_id, auth.Session!, request);
    return Results.Json(result.Body, HubJson.Options, statusCode: result.HttpStatus);
});

app.Run();

static HubAuthenticationResult Authenticate(HttpContext context, Guid storeId, IHubAccessStore access, IClock clock)
{
    var token = HubHttp.BearerToken(context);
    var auth = access.Authenticate(token ?? string.Empty, clock.UtcNow);
    if (auth.IsAuthenticated && auth.Session!.StoreId != storeId)
    {
        return new HubAuthenticationResult("STORE_ACCESS_DENIED");
    }

    return auth;
}

static bool IsOwner(HubAuthenticationResult auth) =>
    auth.Session?.DeviceRole == HubDeviceRole.Hub && auth.Session.OwnerUserId is not null;

static IResult AuthError(string code) => code switch
{
    "DEVICE_REVOKED" or "DEVICE_NOT_PAIRED" or "SESSION_REVOKED" or "STORE_ACCESS_DENIED" =>
        HubHttp.Error(403, code, "The device or session is not authorized."),
    "SESSION_EXPIRED" => HubHttp.Error(401, code, "The session has expired.", "RESUME_SESSION"),
    _ => HubHttp.Error(401, "SESSION_INVALID", "A valid session token is required.")
};

static object SessionBody(HubSession session) => new
{
    session_id = session.SessionId,
    store_id = session.StoreId,
    device_id = session.DeviceId,
    device_role = session.DeviceRole.ToString().ToUpperInvariant(),
    owner_user_id = session.OwnerUserId,
    issued_at = session.IssuedAt,
    expires_at = session.ExpiresAt,
    status = session.Status.ToString().ToUpperInvariant()
};

static object DeviceBody(HubDevice device) => new
{
    store_id = device.StoreId,
    device_id = device.DeviceId,
    device_role = device.DeviceRole.ToString().ToUpperInvariant(),
    pairing_status = device.PairingStatus.ToString().ToUpperInvariant(),
    owner_user_id = device.OwnerUserId,
    label = device.Label,
    platform = device.Platform,
    device_public_key = device.DevicePublicKey
};

static object LockBody(DraftLock current) => new
{
    lock_id = current.LockId,
    store_id = current.StoreId,
    customer_phone = current.CustomerPhone,
    invoice_id = current.InvoiceId,
    device_id = current.DeviceId,
    acquired_at = current.AcquiredAt,
    last_activity_at = current.LastActivityAt,
    timeout_seconds = current.TimeoutSeconds,
    expires_at = current.ExpiresAt,
    state = current.State.ToString().ToUpperInvariant(),
    release_reason = current.ReleaseReason?.ToString().ToUpperInvariant(),
    released_at = current.ReleasedAt
};

public partial class Program
{
}
