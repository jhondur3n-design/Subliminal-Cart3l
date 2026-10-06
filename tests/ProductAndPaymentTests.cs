using SubliminalCart3lBot.Services;
using Xunit;

namespace SubliminalCart3lBot.Tests;

public sealed class ProductAndPaymentTests
{
    [Fact]
    public async Task CatalogReturnsActiveProductsAndFindsProductsById()
    {
        await using var store = await TestStore.CreateAsync();
        var activeId = await store.Database.AddProductAsync("Active", "Visible", 12, "wav-active");
        var inactiveId = await store.Database.AddProductAsync(
            "Inactive", "Hidden", 17, "wav-inactive", isActive: false);

        var products = await store.Catalog.GetAvailableAsync();

        var product = Assert.Single(products);
        Assert.Equal(activeId, product.Id);
        Assert.DoesNotContain(products, item => item.Id == inactiveId);
        Assert.Equal("Inactive", (await store.Catalog.FindAsync(inactiveId))!.Name);
        Assert.True(await store.Catalog.SetActiveAsync(inactiveId, true));
        Assert.Equal(2, (await store.Catalog.GetAvailableAsync()).Count);
    }

    [Fact]
    public async Task CatalogHandlesInvalidProductIdsSafely()
    {
        await using var store = await TestStore.CreateAsync();

        Assert.Null(await store.Catalog.FindAsync(-1));
        Assert.False(await store.Catalog.SetActiveAsync(-1, true));
    }

    [Fact]
    public async Task SuccessfulStarsPaymentMarksOrderPaidAndSelectsItsProductFile()
    {
        await using var store = await TestStore.CreateAsync();
        const long customerId = 12345;
        await store.Database.UpsertCustomerAsync(customerId, "buyer", "Buyer");
        var firstId = await store.Database.AddProductAsync("First WAV", "First", 21, "file-first");
        await store.Database.AddProductAsync("Second WAV", "Second", 34, "file-second");
        var firstProduct = await store.Catalog.FindAsync(firstId);
        var payload = await store.Database.CreatePendingOrderAsync(customerId, firstProduct!);

        var validation = await store.Database.ValidateCheckoutAsync(payload, customerId, "XTR", 21);
        var payment = await store.Database.CompletePaymentAsync(payload, customerId, "XTR", 21, "charge-first");

        Assert.True(validation.IsValid);
        Assert.Equal("paid", payment.Result);
        Assert.Equal("First WAV", payment.ProductName);
        Assert.Equal("file-first", payment.TelegramFileId);
        Assert.True(await store.Database.TryClaimDeliveryAsync(payment.OrderId));
        await store.Database.SetDeliveryStatusAsync(payment.OrderId, delivered: true);
        Assert.False(await store.Database.TryClaimDeliveryAsync(payment.OrderId));
    }

    [Fact]
    public async Task DuplicateTelegramChargeCannotPayOrFulfillAnotherOrder()
    {
        await using var store = await TestStore.CreateAsync();
        const long customerId = 23456;
        await store.Database.UpsertCustomerAsync(customerId, null, "Buyer");
        var firstId = await store.Database.AddProductAsync("First", "First", 10, "file-one");
        var secondId = await store.Database.AddProductAsync("Second", "Second", 20, "file-two");
        var first = await store.Catalog.FindAsync(firstId);
        var second = await store.Catalog.FindAsync(secondId);
        var firstPayload = await store.Database.CreatePendingOrderAsync(customerId, first!);
        var secondPayload = await store.Database.CreatePendingOrderAsync(customerId, second!);

        var firstPayment = await store.Database.CompletePaymentAsync(
            firstPayload, customerId, "XTR", 10, "same-charge-id");
        var duplicateCharge = await store.Database.CompletePaymentAsync(
            secondPayload, customerId, "XTR", 20, "same-charge-id");

        Assert.Equal("paid", firstPayment.Result);
        Assert.Equal("invalid", duplicateCharge.Result);
        Assert.Equal("First", firstPayment.ProductName);
        Assert.Equal("file-one", firstPayment.TelegramFileId);
        Assert.False(await store.Database.TryClaimDeliveryAsync(duplicateCharge.OrderId));
    }
}