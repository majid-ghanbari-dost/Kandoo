using System.Globalization;
using System.Text.Json;
using Kandoo.Core;
using Microsoft.Data.Sqlite;

namespace Kandoo.Hub;

public sealed partial class SqliteHubStore
{
    public ModeBReviewRecord? GetModeBReview(string reviewId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewId);
        lock (_gate)
        {
            ThrowIfDisposed();
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT review_id, native_store_id, source_json, queue_status, operator_phone, issued_invoice_id
                FROM mode_b_reviews WHERE review_id = $review;
                """;
            AddReview(command, "$review", reviewId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadReview(reader) : null;
        }
    }

    public IReadOnlyList<ModeBReviewRecord> ListModeBReviews(Guid nativeStoreId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT review_id, native_store_id, source_json, queue_status, operator_phone, issued_invoice_id
                FROM mode_b_reviews WHERE native_store_id = $store ORDER BY rowid;
                """;
            AddReview(command, "$store", nativeStoreId.ToString("D"));
            using var reader = command.ExecuteReader();
            var reviews = new List<ModeBReviewRecord>();
            while (reader.Read())
            {
                reviews.Add(ReadReview(reader));
            }

            return reviews;
        }
    }

    public void SaveModeBReview(ModeBReviewRecord review, ModeBReviewCorrection? correction = null)
    {
        if (review.NativeStoreId == Guid.Empty || string.IsNullOrWhiteSpace(review.ReviewId) ||
            review.Source.ValueKind != JsonValueKind.Object ||
            review.QueueStatus is not ("REVIEW" or "ISSUED"))
        {
            throw new ArgumentException("A valid Mode-B review is required.", nameof(review));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            try
            {
                using (var command = ReviewCommand(transaction, """
                    INSERT INTO mode_b_reviews
                        (review_id, native_store_id, source_json, queue_status, operator_phone, issued_invoice_id, updated_at)
                    VALUES ($review, $store, $source, $status, $phone, $invoice, $updated)
                    ON CONFLICT(review_id) DO UPDATE SET
                        source_json = excluded.source_json, queue_status = excluded.queue_status,
                        operator_phone = excluded.operator_phone, issued_invoice_id = excluded.issued_invoice_id,
                        updated_at = excluded.updated_at;
                    """))
                {
                    AddReview(command, "$review", review.ReviewId);
                    AddReview(command, "$store", review.NativeStoreId.ToString("D"));
                    AddReview(command, "$source", CanonicalJson.Serialize(review.Source));
                    AddReview(command, "$status", review.QueueStatus);
                    AddReview(command, "$phone", review.OperatorPhone);
                    AddReview(command, "$invoice", review.IssuedInvoiceId?.ToString("D"));
                    AddReview(command, "$updated", ReviewDate(_clock.UtcNow));
                    command.ExecuteNonQuery();
                }

                if (correction is not null)
                {
                    if (correction.ReviewId != review.ReviewId || string.IsNullOrWhiteSpace(correction.OperatorId))
                    {
                        throw new ArgumentException("The correction audit does not belong to the review.", nameof(correction));
                    }

                    using var audit = ReviewCommand(transaction, """
                        INSERT INTO mode_b_review_corrections
                            (review_id, field_path, old_value_json, new_value_json, operator_id, confirmed_at)
                        VALUES ($review, $field, $old, $new, $operator, $confirmed);
                        """);
                    AddReview(audit, "$review", correction.ReviewId);
                    AddReview(audit, "$field", correction.FieldPath);
                    AddReview(audit, "$old", correction.OldValue);
                    AddReview(audit, "$new", correction.NewValue);
                    AddReview(audit, "$operator", correction.OperatorId);
                    AddReview(audit, "$confirmed", ReviewDate(correction.ConfirmedAt));
                    audit.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    private void InitializeModeBReviewSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS mode_b_reviews (
                review_id TEXT PRIMARY KEY,
                native_store_id TEXT NOT NULL,
                source_json TEXT NOT NULL,
                queue_status TEXT NOT NULL,
                operator_phone TEXT NULL,
                issued_invoice_id TEXT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_mode_b_reviews_store ON mode_b_reviews(native_store_id);
            CREATE TABLE IF NOT EXISTS mode_b_review_corrections (
                audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
                review_id TEXT NOT NULL REFERENCES mode_b_reviews(review_id) ON DELETE CASCADE,
                field_path TEXT NOT NULL,
                old_value_json TEXT NULL,
                new_value_json TEXT NOT NULL,
                operator_id TEXT NOT NULL,
                confirmed_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static ModeBReviewRecord ReadReview(SqliteDataReader reader)
    {
        using var document = JsonDocument.Parse(reader.GetString(2));
        return new ModeBReviewRecord(
            reader.GetString(0),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(3),
            document.RootElement.Clone(),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)));
    }

    private static SqliteCommand ReviewCommand(SqliteTransaction transaction, string sql)
    {
        var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void AddReview(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string ReviewDate(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
