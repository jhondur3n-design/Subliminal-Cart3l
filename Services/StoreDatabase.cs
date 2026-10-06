using Microsoft.Data.Sqlite;

namespace SubliminalCart3lBot.Services;

public sealed record ProductItem(
    long Id,
    string Name,
    string Description,
    int StarsPrice,
    string TelegramFileId,
    bool IsActive);

public sealed record CheckoutValidation(bool IsValid, string ErrorMessage);

public sealed record PaymentCompletion(
    string Result,
    long OrderId = 0,
    string ProductName = "",
    string TelegramFileId = "");

public sealed class StoreDatabase
{
    private readonly string _connectionString;

    public StoreDatabase(string contentRoot, string databasePath)
    {
        var resolvedPath = Path.IsPathRooted(databasePath)
            ? databasePath
            : Path.Combine(contentRoot, databasePath);
        var fullPath = Path.GetFullPath(resolvedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS Customers (
                TelegramUserId INTEGER PRIMARY KEY,
                Username TEXT NULL,
                FirstName TEXT NOT NULL DEFAULT '',
                UpdatedAt INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL,
                StarsPrice INTEGER NOT NULL CHECK (StarsPrice > 0),
                TelegramFileId TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                InvoicePayload TEXT NOT NULL UNIQUE,
                TelegramUserId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                AmountXtr INTEGER NOT NULL,
                Currency TEXT NOT NULL DEFAULT 'XTR',
                Status TEXT NOT NULL DEFAULT 'pending',
                TelegramPaymentChargeId TEXT NULL UNIQUE,
                DeliveryStatus TEXT NOT NULL DEFAULT 'pending',
                CreatedAt INTEGER NOT NULL,
                PaidAt INTEGER NULL,
                DeliveredAt INTEGER NULL,
                FOREIGN KEY (TelegramUserId) REFERENCES Customers(TelegramUserId),
                FOREIGN KEY (ProductId) REFERENCES Products(Id)
            );
            CREATE INDEX IF NOT EXISTS IX_Orders_TelegramUserId ON Orders(TelegramUserId);
            CREATE INDEX IF NOT EXISTS IX_Orders_Status ON Orders(Status);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertCustomerAsync(
        long telegramUserId,
        string? username,
        string firstName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Customers (TelegramUserId, Username, FirstName, UpdatedAt)
            VALUES ($userId, $username, $firstName, $now)
            ON CONFLICT(TelegramUserId) DO UPDATE SET
                Username = excluded.Username,
                FirstName = excluded.FirstName,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$userId", telegramUserId);
        command.Parameters.AddWithValue("$username", (object?)username ?? DBNull.Value);
        command.Parameters.AddWithValue("$firstName", firstName);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductItem>> GetActiveProductsAsync(
        CancellationToken cancellationToken = default)
    {
        var products = new List<ProductItem>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Description, StarsPrice, TelegramFileId, IsActive
            FROM Products WHERE IsActive = 1 ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(ReadProduct(reader));
        }
        return products;
    }

    public async Task<ProductItem?> GetProductAsync(
        long productId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Description, StarsPrice, TelegramFileId, IsActive
            FROM Products WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", productId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProduct(reader) : null;
    }

    public async Task<bool> SetProductActiveAsync(
        long productId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Products SET IsActive = $active WHERE Id = $id;";
        command.Parameters.AddWithValue("$active", isActive ? 1 : 0);
        command.Parameters.AddWithValue("$id", productId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<long> AddProductAsync(
        string name,
        string description,
        int starsPrice,
        string telegramFileId,
        CancellationToken cancellationToken = default,
        bool isActive = true)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Products (Name, Description, StarsPrice, TelegramFileId, IsActive, CreatedAt)
            VALUES ($name, $description, $price, $fileId, $active, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$price", starsPrice);
        command.Parameters.AddWithValue("$fileId", telegramFileId);
        command.Parameters.AddWithValue("$active", isActive ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<string> CreatePendingOrderAsync(
        long telegramUserId,
        ProductItem product,
        CancellationToken cancellationToken = default)
    {
        var payload = $"order:{Guid.NewGuid():N}";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Orders (InvoicePayload, TelegramUserId, ProductId, AmountXtr, Currency, CreatedAt)
            VALUES ($payload, $userId, $productId, $amount, 'XTR', $now);
            """;
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$userId", telegramUserId);
        command.Parameters.AddWithValue("$productId", product.Id);
        command.Parameters.AddWithValue("$amount", product.StarsPrice);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return payload;
    }

    public async Task<CheckoutValidation> ValidateCheckoutAsync(
        string payload,
        long telegramUserId,
        string currency,
        int totalAmount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.TelegramUserId, o.AmountXtr, o.Currency, o.Status, o.CreatedAt,
                   p.StarsPrice, p.IsActive, p.TelegramFileId
            FROM Orders o JOIN Products p ON p.Id = o.ProductId
            WHERE o.InvoicePayload = $payload;
            """;
        command.Parameters.AddWithValue("$payload", payload);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new(false, "This invoice is no longer available.");
        }

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4));
        var valid = reader.GetInt64(0) == telegramUserId
            && reader.GetInt32(1) == totalAmount
            && reader.GetString(2) == "XTR"
            && currency == "XTR"
            && reader.GetString(3) == "pending"
            && reader.GetInt32(5) == totalAmount
            && reader.GetInt64(6) == 1
            && !string.IsNullOrWhiteSpace(reader.GetString(7))
            && DateTimeOffset.UtcNow - createdAt <= TimeSpan.FromHours(24);

        return valid
            ? new(true, "")
            : new(false, "The product, price, or invoice changed. Return to the store and try again.");
    }

    public async Task<PaymentCompletion> CompletePaymentAsync(
        string payload,
        long telegramUserId,
        string currency,
        int totalAmount,
        string chargeId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT o.Id, o.TelegramUserId, o.AmountXtr, o.Currency, o.Status,
                   o.TelegramPaymentChargeId, p.Name, p.TelegramFileId, p.StarsPrice, p.IsActive
            FROM Orders o JOIN Products p ON p.Id = o.ProductId
            WHERE o.InvoicePayload = $payload;
            """;
        select.Parameters.AddWithValue("$payload", payload);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new("invalid");
        }

        var orderId = reader.GetInt64(0);
        var orderUserId = reader.GetInt64(1);
        var orderAmount = reader.GetInt32(2);
        var orderCurrency = reader.GetString(3);
        var status = reader.GetString(4);
        var priorChargeId = reader.IsDBNull(5) ? null : reader.GetString(5);
        var productName = reader.GetString(6);
        var fileId = reader.GetString(7);
        var currentPrice = reader.GetInt32(8);
        var active = reader.GetInt64(9) == 1;
        await reader.DisposeAsync();

        if (status == "paid")
        {
            return priorChargeId == chargeId
                ? new("duplicate", orderId, productName, fileId)
                : new("invalid");
        }

        if (status != "pending" || orderUserId != telegramUserId || orderAmount != totalAmount
            || currentPrice != totalAmount || orderCurrency != "XTR" || currency != "XTR"
            || !active || string.IsNullOrWhiteSpace(fileId))
        {
            return new("invalid");
        }

        await using var duplicate = connection.CreateCommand();
        duplicate.Transaction = transaction;
        duplicate.CommandText = "SELECT EXISTS(SELECT 1 FROM Orders WHERE TelegramPaymentChargeId = $chargeId);";
        duplicate.Parameters.AddWithValue("$chargeId", chargeId);
        if (Convert.ToInt64(await duplicate.ExecuteScalarAsync(cancellationToken)) != 0)
        {
            return new("invalid");
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE Orders SET Status = 'paid', TelegramPaymentChargeId = $chargeId, PaidAt = $now
            WHERE Id = $id AND Status = 'pending';
            """;
        update.Parameters.AddWithValue("$chargeId", chargeId);
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        update.Parameters.AddWithValue("$id", orderId);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return new("invalid");
        }

        await transaction.CommitAsync(cancellationToken);
        return new("paid", orderId, productName, fileId);
    }

    public async Task SetDeliveryStatusAsync(
        long orderId,
        bool delivered,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders SET DeliveryStatus = $status, DeliveredAt = $deliveredAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$status", delivered ? "delivered" : "failed");
        command.Parameters.AddWithValue(
            "$deliveredAt",
            delivered ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : DBNull.Value);
        command.Parameters.AddWithValue("$id", orderId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryClaimDeliveryAsync(
        long orderId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders SET DeliveryStatus = 'sending'
            WHERE Id = $id AND Status = 'paid' AND DeliveryStatus IN ('pending', 'failed');
            """;
        command.Parameters.AddWithValue("$id", orderId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static ProductItem ReadProduct(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt32(3),
        reader.GetString(4),
        reader.GetInt64(5) == 1);
}