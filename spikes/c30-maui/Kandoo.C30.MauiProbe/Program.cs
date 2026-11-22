using Kandoo.C30.Maui;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Kandoo.C30.MauiProbe <openapi-url> <evidence-path>");
    return 2;
}

return await HubProbe.RunAsync(args[0], args[1]);
