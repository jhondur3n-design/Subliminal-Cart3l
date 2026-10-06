using System.Collections.Concurrent;

namespace SubliminalCart3lBot.Services;

public sealed record NewProduct(string Name, string Description, int StarsPrice);

public sealed class ProductCatalog(StoreDatabase database)
{
    private readonly ConcurrentDictionary<long, NewProduct> _waitingForWav = new();

    public Task<IReadOnlyList<ProductItem>> GetAvailableAsync(CancellationToken cancellationToken = default)
        => database.GetActiveProductsAsync(cancellationToken);

    public Task<ProductItem?> FindAsync(long productId, CancellationToken cancellationToken = default)
        => database.GetProductAsync(productId, cancellationToken);

    public Task<bool> SetActiveAsync(
        long productId,
        bool isActive,
        CancellationToken cancellationToken = default)
        => database.SetProductActiveAsync(productId, isActive, cancellationToken);

    public bool BeginWavUpload(long ownerId, NewProduct product)
        => _waitingForWav.TryAdd(ownerId, product);

    public async Task<long?> CompleteWavUploadAsync(
        long ownerId,
        string fileName,
        string telegramFileId,
        CancellationToken cancellationToken = default)
    {
        if (!fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            || !_waitingForWav.TryRemove(ownerId, out var product))
        {
            return null;
        }

        return await database.AddProductAsync(
            product.Name,
            product.Description,
            product.StarsPrice,
            telegramFileId,
            cancellationToken);
    }
}