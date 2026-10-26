using UnityEngine;
using AnonTokyo.Shop;

// Fixed denominations for conservation, migration and transaction regression cases.
// Live catalog prices and new-game progression are verified in ShopBalanceChecks.
public static class ShopLedgerFixture
{
    public static ShopCatalog Catalog(ShopCatalog source)
    {
        var copy=Object.Instantiate(source);
        Reset(copy);
        return copy;
    }
    public static void Reset(ShopCatalog copy)
    {
        foreach(var product in copy.products)
            if(ShopProductSet.IsCurrentProduct(product.id))
            {product.buyPrice=5;product.sellPrice=10;product.batchSize=20;}
    }
}
