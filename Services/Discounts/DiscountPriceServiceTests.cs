using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using LiliShop.Domain.Entities;
using LiliShop.Domain.Entities.DiscountSystem;
using Xunit;

namespace Lili.Shop.Tests.Services.Discounts
{
    /* =============================================================================
       Coverage Matrix for DiscountPriceService Tests
       =============================================================================
       Method                        | Scenario                                       | Test Method
       ------------------------------|------------------------------------------------|-----------------------------------------
       CalculateEffectivePrice       | Fixed amount, product has PreviousPrice        | WithFixedAmountAndPreviousPrice_...
                                     | Percentage, no PreviousPrice                   | WithPercentage_ReturnsCorrectPrice
                                     | Fixed amount, no PreviousPrice                 | WithFixedAmount_ReturnsCorrectPrice
                                     | PreviousPrice set, new % discount              | WithPreviousPrice_CalculatesFromPreviousPrice
                                     | Zero discount                                  | WithZeroDiscount_ReturnsUnchangedPrice
                                     | Null tier (safe fallback)                      | WithTier_NullTier_ReturnsProductPrice
                                     | Null amount (safe fallback)                    | WithAmount_NullAmount_ReturnsProductPrice
       ------------------------------|------------------------------------------------|-----------------------------------------
       ResolveTierForProduct         | Single (non‑group) discount                    | WithSingleDiscount_ReturnsNull
                                     | Group – All                                    | WithGroupTargetAll_ReturnsTier
                                     | Group – ProductBrand (match / mismatch)        | WithGroupTargetBrand_MatchesCorrectBrand
                                     | Group – Size (match / mismatch)                | WithSizeClassification_MatchesCorrectSize
                                     | Group – ProductType (match / mismatch)         | WithProductType_MatchesCorrectType
                                     | Group – multiple AND conditions                | WithMultipleConditions_AllMustMatch
                                     | Group – null ConditionGroups                   | WithDiscountGroupButNoConditionGroups_ReturnsNull
       ------------------------------|------------------------------------------------|-----------------------------------------
       ApplyBestDiscountsTo...       | Excluded single discount ignored               | WithExcludedSingleDiscount_IgnoresExcludedDiscount
                                     | Single overrides group (policy)                | SingleAlwaysOverridesGroup_EvenIfGroupIsCheaper
                                     | Multiple group overlaps – best price wins      | MultipleGroupOverlaps_PicksLowestPrice
                                     | Inactive / expired / future ignored            | IgnoresInactiveAndExpiredDiscounts
                                     | Exclude discount ID ignored                    | WithExcludedDiscountId_IgnoresExcludedId
                                     | Mixed products (single / group / none)         | ProcessesMultipleProductsCorrectly
                                     | Empty product list                             | EmptyList_ReturnsWithoutError
                                     | Already‑discounted product gets better discount| ProductWithPreviousPrice_GetsBetterDiscount
       ------------------------------|------------------------------------------------|-----------------------------------------
       RestorePricesForAffected...   | Single discount – revert price                 | SingleDiscount_RevertsPriceAndClearsPreviousPrice
                                     | Group discount – revert price                  | GroupDiscount_RevertsPricesOfAffectedProducts
                                     | Product without PreviousPrice – unchanged      | GroupDiscount_ProductWithNoPreviousPrice_StillReturned
       ============================================================================= */
    public class DiscountPriceServiceTests : DiscountPriceServiceTestBase
    {
        #region Private Helpers

        /// <summary>
        /// Creates a group discount mock with a single condition group and a single condition.
        /// The condition is set to the given <paramref name="targetType"/> and <paramref name="targetId"/>,
        /// and the entire condition group is linked to the provided <paramref name="tier"/>.
        /// </summary>
        /// <remarks>
        /// This helper is used to test the following scenarios:
        /// <list type="bullet">
        ///   <item><description><see cref="DiscountPriceService.ResolveTierForProduct"/> – matching a product by brand, type, size, or all.</description></item>
        ///   <item><description><see cref="DiscountPriceService.ApplyBestDiscountsToRestoredProductsAsync"/> – evaluating group discounts against products.</description></item>
        ///   <item><description><see cref="DiscountPriceService.RestorePricesForAffectedProductsAsync"/> – restoring prices affected by a group discount.</description></item>
        /// </list>
        /// The discount returned has no <c>Id</c> and no <c>IsActive</c> flag set, allowing the caller to set them as needed for the specific test.
        /// </remarks>
        private Discount CreateGroupDiscountMock(DiscountTargetType targetType, int? targetId, DiscountTier tier)
        {
            return new Discount
            {
                DiscountGroup = new DiscountGroup
                {
                    ConditionGroups = new List<ConditionGroup>
                    {
                        new ConditionGroup
                        {
                            DiscountTier = tier,
                            DiscountGroupConditions = new List<DiscountGroupCondition>
                            {
                                new DiscountGroupCondition
                                {
                                    TargetEntity = targetType,
                                    ProductBrandId = targetType == DiscountTargetType.ProductBrand ? targetId : null,
                                    ProductTypeId = targetType == DiscountTargetType.ProductType ? targetId : null,
                                    SizeClassificationId = targetType == DiscountTargetType.Size ? targetId : null,
                                    ProductId = targetType == DiscountTargetType.Product ? targetId : null,
                                }
                            }
                        }
                    }
                }
            };
        }

        #endregion

        #region CalculateEffectivePrice

        /// <summary>
        /// Verifies that a fixed‑amount discount is calculated from the <c>PreviousPrice</c> (the original base price)
        /// when the product is already on sale.
        /// </summary>
        /// <remarks>
        /// Scenario: The product’s current price is $90, but its original price (PreviousPrice) is $100.
        /// Applying a flat $15 discount should result in $85, because the discount engine always uses
        /// the base price (<c>PreviousPrice ?? Price</c>).
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "CalculateEffectivePrice")]
        public void CalculateEffectivePrice_WithFixedAmountAndPreviousPrice_CalculatesFromPreviousPrice()
        {
            var product = new Product { Price = 90m, PreviousPrice = 100m };

            // Passing 15m and false for flat amount
            var result = _sut.CalculateEffectivePrice(product, 15m, false);

            result.Should().Be(85m); // 100 - 15 = 85
        }

        /// <summary>
        /// Verifies that a percentage discount is correctly applied when the product has no <c>PreviousPrice</c>.
        /// </summary>
        /// <remarks>
        /// Scenario: Product price is $100, a 20% discount is applied. The effective price should be $80.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "CalculateEffectivePrice")]
        public void CalculateEffectivePrice_WithPercentage_ReturnsCorrectPrice()
        {
            var product = new Product { Price = 100m, PreviousPrice = null };
            var result = _sut.CalculateEffectivePrice(product, 20m, true); // 20% off
            result.Should().Be(80m);
        }

        /// <summary>
        /// Verifies that a fixed-amount discount is correctly applied when the product has no <c>PreviousPrice</c>.
        /// The discount engine uses the current <c>Price</c> as the base.
        /// </summary>
        /// <remarks>
        /// Scenario: Product price is $100 and a flat $15 discount is applied. The effective price should be $85.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "CalculateEffectivePrice")]
        public void CalculateEffectivePrice_WithFixedAmount_ReturnsCorrectPrice()
        {
            var product = new Product { Price = 100m, PreviousPrice = null };
            var result = _sut.CalculateEffectivePrice(product, 15m, false); // $15 off
            result.Should().Be(85m);
        }

        /// <summary>
        /// Verifies that when a product already has a <c>PreviousPrice</c> (i.e., it is already on sale),
        /// a new percentage discount is calculated from that original base price, not from the current <c>Price</c>.
        /// </summary>
        /// <remarks>
        /// Scenario: Product is currently $80 (on sale from $100), and a 50% discount is applied.
        /// The effective price must be 50% off the base $100, i.e., $50, not 50% off $80.
        /// This ensures the discount engine always uses the true original price (<c>PreviousPrice ?? Price</c>).
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "CalculateEffectivePrice")]
        public void CalculateEffectivePrice_WithPreviousPrice_CalculatesFromPreviousPrice()
        {
            // If product is currently on sale ($80), but its base was $100, 
            // a new 50% discount must evaluate against the $100 base.
            var product = new Product { Price = 80m, PreviousPrice = 100m };
            var result = _sut.CalculateEffectivePrice(product, 50m, true);
            result.Should().Be(50m);
        }

        /// <summary>
        /// Verifies that a discount of zero (either amount or percentage) does not change the product price.
        /// </summary>
        /// <remarks>
        /// Scenario: Product price is $150 and a $0 flat discount is applied. The effective price must remain $150.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "CalculateEffectivePrice")]
        public void CalculateEffectivePrice_WithZeroDiscount_ReturnsUnchangedPrice()
        {
            var product = new Product { Price = 150m };
            var result = _sut.CalculateEffectivePrice(product, 0m, false);
            result.Should().Be(150m);
        }

        /// <summary>
        /// Verifies that when a <c>null</c> tier is passed to <c>CalculateEffectivePrice</c>,
        /// the product's current price is returned unchanged.
        /// </summary>
        /// <remarks>
        /// Scenario: The tier is <c>null</c>, meaning no discount applies. The method should simply return
        /// the existing <c>product.Price</c> as a safe fallback.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "CalculateEffectivePrice")]
        public void CalculateEffectivePrice_WithTier_NullTier_ReturnsProductPrice()
        {
            var product = new Product { Price = 100m };
            var result = _sut.CalculateEffectivePrice(product, (DiscountTier)null);
            result.Should().Be(100m);
        }

        /// <summary>
        /// Verifies that when a <c>null</c> amount is passed to the simplified <c>CalculateEffectivePrice</c> overload,
        /// the product's current price is returned unchanged, acting as a safe fallback.
        /// </summary>
        /// <remarks>
        /// Scenario: The discount amount is <c>null</c>, meaning no discount should be applied.
        /// The method should return the existing <c>product.Price</c> without any calculation or error.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "CalculateEffectivePrice")]
        public void CalculateEffectivePrice_WithAmount_NullAmount_ReturnsProductPrice()
        {
            var product = new Product { Price = 100m };
            var result = _sut.CalculateEffectivePrice(product, (decimal?)null, false);
            result.Should().Be(100m);
        }

        #endregion

        #region ResolveTierForProduct

        /// <summary>
        /// Verifies that a single (non‑group) discount returns <c>null</c> from <c>ResolveTierForProduct</c>,
        /// because single discounts do not use tiers or condition groups.
        /// </summary>
        /// <remarks>
        /// Scenario: The discount has no <c>DiscountGroup</c> (it is a direct product discount).
        /// Regardless of the product, the method should return <c>null</c>, indicating that tiers are not applicable.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ResolveTierForProduct")]
        public void ResolveTierForProduct_WithSingleDiscount_ReturnsNull()
        {
            // Single discounts don't use Tiers
            var discount = new Discount { DiscountGroup = null };
            var product = new Product { Id = 1 };

            var result = _sut.ResolveTierForProduct(discount, product);
            result.Should().BeNull();
        }

        /// <summary>
        /// Verifies that a group discount targeting <c>TargetEntity = All</c> resolves to the correct tier
        /// for any product, regardless of the product's properties.
        /// </summary>
        /// <remarks>
        /// Scenario: A discount group contains a condition with <c>TargetEntity = All</c>.
        /// The method should return the linked <see cref="DiscountTier"/> without checking any product attributes.
        /// This is the simplest and most universal targeting rule.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ResolveTierForProduct")]
        public void ResolveTierForProduct_WithGroupTargetAll_ReturnsTier()
        {
            var expectedTier = new DiscountTier { Id = 10 };
            var discount = CreateGroupDiscountMock(DiscountTargetType.All, null, expectedTier);
            var product = new Product { Id = 99 }; // Random product

            var result = _sut.ResolveTierForProduct(discount, product);
            result.Should().NotBeNull();
            result.Id.Should().Be(10);
        }

        /// <summary>
        /// Verifies that a group discount targeting <c>ProductBrand</c> resolves the tier only for products
        /// with the matching brand ID, and returns <c>null</c> for products with a different brand.
        /// </summary>
        /// <remarks>
        /// Scenario: The discount targets brand ID 5. A product with <c>ProductBrandId = 5</c> should receive the tier;
        /// a product with <c>ProductBrandId = 9</c> should not.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ResolveTierForProduct")]
        public void ResolveTierForProduct_WithGroupTargetBrand_MatchesCorrectBrand()
        {
            var expectedTier = new DiscountTier { Id = 15 };
            var discount = CreateGroupDiscountMock(DiscountTargetType.ProductBrand, 5, expectedTier); // Target Brand 5

            var validProduct = new Product { ProductBrandId = 5 };
            var invalidProduct = new Product { ProductBrandId = 9 };

            _sut.ResolveTierForProduct(discount, validProduct).Should().NotBeNull();
            _sut.ResolveTierForProduct(discount, invalidProduct).Should().BeNull();
        }

        /// <summary>
        /// Verifies that a group discount targeting <c>Size</c> classification resolves the tier only for products
        /// whose <c>ProductCharacteristics</c> collection contains the specified size, and returns <c>null</c> for others.
        /// </summary>
        /// <remarks>
        /// Scenario: The discount targets size 3. A product with a characteristic having <c>SizeClassificationId = 3</c>
        /// should receive the tier; a product with only size 4 should not.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ResolveTierForProduct")]
        public void ResolveTierForProduct_WithSizeClassification_MatchesCorrectSize()
        {
            var expectedTier = new DiscountTier { Id = 20 };
            var discount = CreateGroupDiscountMock(DiscountTargetType.Size, 3, expectedTier); // Target Size 3

            var validProduct = new Product
            {
                ProductCharacteristics = new List<ProductCharacteristic>
                {
                    new ProductCharacteristic { SizeClassificationId = 3 }
                }
            };

            var invalidProduct = new Product
            {
                ProductCharacteristics = new List<ProductCharacteristic>
                {
                    new ProductCharacteristic { SizeClassificationId = 4 }
                }
            };

            _sut.ResolveTierForProduct(discount, validProduct).Should().NotBeNull();
            _sut.ResolveTierForProduct(discount, invalidProduct).Should().BeNull();
        }

        /// <summary>
        /// Verifies that a group discount targeting <c>ProductType</c> resolves the tier only for products
        /// with the matching product type ID, and returns <c>null</c> for products with a different type.
        /// </summary>
        /// <remarks>
        /// Scenario: The discount targets product type 10. A product with <c>ProductTypeId = 10</c> receives the tier;
        /// a product with <c>ProductTypeId = 99</c> does not.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ResolveTierForProduct")]
        public void ResolveTierForProduct_WithProductType_MatchesCorrectType()
        {
            var tier = new DiscountTier { Id = 30 };
            var discount = CreateGroupDiscountMock(DiscountTargetType.ProductType, 10, tier);
            var validProduct = new Product { ProductTypeId = 10 };
            var invalidProduct = new Product { ProductTypeId = 99 };

            _sut.ResolveTierForProduct(discount, validProduct).Should().Be(tier);
            _sut.ResolveTierForProduct(discount, invalidProduct).Should().BeNull();
        }

        /// <summary>
        /// Verifies that a group discount with multiple conditions (e.g., brand AND type) requires all conditions to match.
        /// If any condition fails, the tier is not resolved.
        /// </summary>
        /// <remarks>
        /// Scenario: The discount condition group contains two conditions: ProductBrand = 5 AND ProductType = 10.
        /// A product matching both receives the tier; a product matching only one does not.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ResolveTierForProduct")]
        public void ResolveTierForProduct_WithMultipleConditions_AllMustMatch()
        {
            var tier = new DiscountTier { Id = 40 };
            var discount = new Discount
            {
                DiscountGroup = new DiscountGroup
                {
                    ConditionGroups = new List<ConditionGroup>
                    {
                        new ConditionGroup
                        {
                            DiscountTier = tier,
                            DiscountGroupConditions = new List<DiscountGroupCondition>
                            {
                                new DiscountGroupCondition { TargetEntity = DiscountTargetType.ProductBrand, ProductBrandId = 5 },
                                new DiscountGroupCondition { TargetEntity = DiscountTargetType.ProductType, ProductTypeId = 10 }
                            }
                        }
                    }
                }
            };
            var validProduct = new Product { ProductBrandId = 5, ProductTypeId = 10 };
            var invalidProduct = new Product { ProductBrandId = 5, ProductTypeId = 99 };

            _sut.ResolveTierForProduct(discount, validProduct).Should().Be(tier);
            _sut.ResolveTierForProduct(discount, invalidProduct).Should().BeNull();
        }

        /// <summary>
        /// Verifies that a group discount with a <c>DiscountGroup</c> that has no <c>ConditionGroups</c>
        /// (or <c>null</c>) returns <c>null</c> from <c>ResolveTierForProduct</c>, because there are
        /// no rules to evaluate.
        /// </summary>
        /// <remarks>
        /// Scenario: The discount has a <c>DiscountGroup</c>, but its <c>ConditionGroups</c> collection is <c>null</c>.
        /// Without any condition groups, no tier can be resolved, so the method should return <c>null</c>.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ResolveTierForProduct")]
        public void ResolveTierForProduct_WithDiscountGroupButNoConditionGroups_ReturnsNull()
        {
            var discount = new Discount
            {
                DiscountGroup = new DiscountGroup { ConditionGroups = null }
            };
            var product = new Product { Id = 1 };
            _sut.ResolveTierForProduct(discount, product).Should().BeNull();
        }

        #endregion

        #region ApplyBestDiscountsToRestoredProductsAsync

        /// <summary>
        /// Verifies that when an <c>excludeDiscountId</c> is provided to <see cref="DiscountPriceService.ApplyBestDiscountsToRestoredProductsAsync"/>,
        /// the specified discount is completely ignored—even if it matches the product and offers a better price.
        /// </summary>
        /// <remarks>
        /// Scenario: A single discount (ID 5) applies a 50% discount, which would reduce the price from $100 to $50.
        /// However, because discount ID 5 is explicitly excluded, the method must not apply it.
        /// The product remains at its original price ($100), and <c>PreviousPrice</c> stays <c>null</c>.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ApplyBestDiscountsToRestoredProductsAsync")]
        [Trait("Scenario", "ExcludeDiscount")]
        public async Task ApplyBestDiscounts_WithExcludedSingleDiscount_IgnoresExcludedDiscount()
        {
            var product = new Product { Id = 1, Price = 100m, PreviousPrice = null };

            var singleDiscount = new Discount
            {
                Id = 5,
                IsActive = true,
                Amount = 50m,
                IsPercentage = true,
                DiscountGroup = null,
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1 } }
            };

            MockDiscountDbSet(new List<Discount> { singleDiscount });
            var restoredProducts = new List<Product> { product };

            // Act: Explicitly exclude ID 5
            await _sut.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, excludeDiscountId: 5);

            // Assert
            product.Price.Should().Be(100m);
            product.PreviousPrice.Should().BeNull();
        }

        /// <summary>
        /// Verifies that when a product is covered by both a single (direct) discount and a group discount,
        /// the single discount always takes priority, even if the group discount would result in a lower price.
        /// </summary>
        /// <remarks>
        /// Scenario: The single discount offers $10 off ($100 → $90), while the group discount offers $50 off ($100 → $50).
        /// Because the product has a direct single discount, it must receive the single discount price ($90),
        /// ignoring the cheaper group discount. This ensures that single‑discount assignments are honoured as the primary pricing rule.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ApplyBestDiscountsToRestoredProductsAsync")]
        [Trait("Scenario", "SingleOverridesGroup")]
        public async Task ApplyBestDiscounts_SingleAlwaysOverridesGroup_EvenIfGroupIsCheaper()
        {
            // Arrange
            var product = new Product { Id = 1, Price = 100m, PreviousPrice = null };

            // Single Discount: $10 off -> Price $90
            var singleDiscount = new Discount
            {
                Id = 1,
                IsActive = true,
                Amount = 10m,
                IsPercentage = false,
                DiscountGroup = null,
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1 } }
            };

            // Group Discount: $50 off -> Price $50 (Normally cheaper, but Single must win!)
            var groupDiscount = CreateGroupDiscountMock(DiscountTargetType.All, null, new DiscountTier { Amount = 50m, IsPercentage = false });
            groupDiscount.Id = 2;
            groupDiscount.IsActive = true;

            MockDiscountDbSet(new List<Discount> { singleDiscount, groupDiscount });

            // Act
            await _sut.ApplyBestDiscountsToRestoredProductsAsync(new List<Product> { product });

            // Assert: The single discount ($90) wins over the group discount ($50)
            product.Price.Should().Be(90m);
            product.PreviousPrice.Should().Be(100m);
        }

        /// <summary>
        /// Verifies that when multiple group discounts apply to the same product,
        /// the best‑price engine picks the lowest calculated price among them.
        /// </summary>
        /// <remarks>
        /// Scenario: Two group discounts both target the product via <c>All</c>.
        /// Group 1 gives 10% off ($90) and Group 2 gives a flat $25 off ($75).
        /// The engine must select $75, because it’s the lowest price.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ApplyBestDiscountsToRestoredProductsAsync")]
        [Trait("Scenario", "MultipleGroupOverlaps")]
        public async Task ApplyBestDiscounts_MultipleGroupOverlaps_PicksLowestPrice()
        {
            // Arrange
            var product = new Product { Id = 1, Price = 100m, PreviousPrice = null };

            // Group 1: 10% off ($90)
            var group1 = CreateGroupDiscountMock(DiscountTargetType.All, null, new DiscountTier { Amount = 10m, IsPercentage = true });
            group1.Id = 1; group1.IsActive = true;

            // Group 2: $25 flat off ($75) -> WINNER
            var group2 = CreateGroupDiscountMock(DiscountTargetType.All, null, new DiscountTier { Amount = 25m, IsPercentage = false });
            group2.Id = 2; group2.IsActive = true;

            MockDiscountDbSet(new List<Discount> { group1, group2 });

            // Act
            await _sut.ApplyBestDiscountsToRestoredProductsAsync(new List<Product> { product });

            // Assert
            product.Price.Should().Be(75m);
        }

        /// <summary>
        /// Verifies that discounts that are inactive, expired, or not yet started are ignored by the best‑price engine.
        /// Only discounts that are currently active and within their valid date range are considered.
        /// </summary>
        /// <remarks>
        /// Scenario: Three discounts are set up, all of which would normally apply to the product:
        /// <list type="bullet">
        ///   <item><description>One with an <c>EndDate</c> in the past (expired).</description></item>
        ///   <item><description>One with a <c>StartDate</c> in the future (not yet active).</description></item>
        ///   <item><description>One with <c>IsActive = false</c> (manually deactivated).</description></item>
        /// </list>
        /// None of them should affect the product’s price, which remains at $100.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ApplyBestDiscountsToRestoredProductsAsync")]
        [Trait("Scenario", "InactiveAndExpiredDiscounts")]
        public async Task ApplyBestDiscounts_IgnoresInactiveAndExpiredDiscounts()
        {
            // Arrange
            var product = new Product { Id = 1, Price = 100m, PreviousPrice = null };

            // Expired yesterday
            var expiredDiscount = new Discount
            {
                Id = 1,
                IsActive = true,
                Amount = 90m,
                IsPercentage = false,
                EndDate = DateTimeOffset.UtcNow.AddDays(-1),
                DiscountGroup = null,
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1 } }
            };

            // Starts tomorrow
            var futureDiscount = new Discount
            {
                Id = 2,
                IsActive = true,
                Amount = 90m,
                IsPercentage = false,
                StartDate = DateTimeOffset.UtcNow.AddDays(1),
                DiscountGroup = null,
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1 } }
            };

            // Inactive flag
            var inactiveDiscount = new Discount
            {
                Id = 3,
                IsActive = false,
                Amount = 90m,
                IsPercentage = false,
                DiscountGroup = null,
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1 } }
            };

            MockDiscountDbSet(new List<Discount> { expiredDiscount, futureDiscount, inactiveDiscount });

            // Act
            await _sut.ApplyBestDiscountsToRestoredProductsAsync(new List<Product> { product });

            // Assert: No valid discounts applied
            product.Price.Should().Be(100m);
            product.PreviousPrice.Should().BeNull();
        }

        /// <summary>
        /// Verifies that when an <c>excludeDiscountId</c> is passed to
        /// <see cref="DiscountPriceService.ApplyBestDiscountsToRestoredProductsAsync"/>,
        /// the discount with that ID is completely ignored even if it matches the product.
        /// </summary>
        /// <remarks>
        /// Scenario: A single discount (ID 99) offers 50% off, reducing the price from $100 to $50.
        /// Because that discount ID is explicitly excluded, the engine must skip it, leaving the product
        /// at its original price ($100) and <c>PreviousPrice</c> unchanged (<c>null</c>).
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ApplyBestDiscountsToRestoredProductsAsync")]
        [Trait("Scenario", "ExcludeDiscountId")]
        public async Task ApplyBestDiscounts_WithExcludedDiscountId_IgnoresExcludedId()
        {
            // Arrange
            var product = new Product { Id = 1, Price = 100m, PreviousPrice = null };

            var discountToExclude = new Discount
            {
                Id = 99,
                IsActive = true,
                Amount = 50m,
                IsPercentage = true,
                DiscountGroup = null,
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1 } }
            };

            MockDiscountDbSet(new List<Discount> { discountToExclude });

            // Act
            await _sut.ApplyBestDiscountsToRestoredProductsAsync(new List<Product> { product }, excludeDiscountId: 99);

            // Assert
            product.Price.Should().Be(100m);
            product.PreviousPrice.Should().BeNull();
        }

        /// <summary>
        /// Verifies that the best‑price engine correctly applies single, group, and no discounts
        /// to a mixed list of products in a single call.
        /// </summary>
        /// <remarks>
        /// Scenario:
        /// <list type="bullet">
        ///   <item><description>Product 1 (brand 1) is covered by a group discount giving $15 off → $85.</description></item>
        ///   <item><description>Product 2 has a direct single discount of $30 off → $70.</description></item>
        ///   <item><description>Product 3 has no matching discount → remains $100.</description></item>
        /// </list>
        /// The method must process all three correctly in one batch.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ApplyBestDiscountsToRestoredProductsAsync")]
        [Trait("Scenario", "MultipleProducts")]
        public async Task ApplyBestDiscounts_ProcessesMultipleProductsCorrectly()
        {
            // Arrange
            var product1 = new Product { Id = 1, Price = 100m, ProductBrandId = 1 }; // Will get Group Discount
            var product2 = new Product { Id = 2, Price = 100m, ProductBrandId = 2 }; // Will get Single Discount
            var product3 = new Product { Id = 3, Price = 100m, ProductBrandId = 3 }; // Gets Nothing

            var groupDiscount = CreateGroupDiscountMock(DiscountTargetType.ProductBrand, 1, new DiscountTier { Amount = 15m, IsPercentage = false });
            groupDiscount.IsActive = true;

            var singleDiscount = new Discount
            {
                Id = 2,
                IsActive = true,
                Amount = 30m,
                IsPercentage = false,
                DiscountGroup = null,
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 2 } }
            };

            MockDiscountDbSet(new List<Discount> { groupDiscount, singleDiscount });

            // Act
            await _sut.ApplyBestDiscountsToRestoredProductsAsync(new List<Product> { product1, product2, product3 });

            // Assert
            product1.Price.Should().Be(85m); // 100 - 15 Group
            product2.Price.Should().Be(70m); // 100 - 30 Single
            product3.Price.Should().Be(100m); // No change
        }

        /// <summary>
        /// Verifies that calling <c>ApplyBestDiscountsToRestoredProductsAsync</c> with an empty product list
        /// does not throw an exception.
        /// </summary>
        /// <remarks>
        /// Scenario: An empty list of restored products is passed. The method should early‑return
        /// without attempting any price calculations or database saves. This guards against
        /// downstream consumers accidentally passing empty batches.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ApplyBestDiscountsToRestoredProductsAsync")]
        [Trait("Scenario", "EmptyProductList")]
        public async Task ApplyBestDiscounts_EmptyList_ReturnsWithoutError()
        {
            MockDiscountDbSet(new List<Discount>());

            await _sut.Awaiting(s => s.ApplyBestDiscountsToRestoredProductsAsync(new List<Product>()))
                .Should().NotThrowAsync();
        }

        /// <summary>
        /// Verifies that when a product already has a <c>PreviousPrice</c> (i.e., it is currently on sale)
        /// and a better discount becomes available, the best‑price engine applies the new discount
        /// correctly from the original base price and updates both <c>Price</c> and <c>PreviousPrice</c>.
        /// </summary>
        /// <remarks>
        /// Scenario: Product 1 has a current price of $80 (base $100 stored in <c>PreviousPrice</c>).
        /// A new single discount of 30% off becomes active. The engine should calculate 30% off the base $100,
        /// resulting in $70, set <c>Price = 70</c>, and keep <c>PreviousPrice = 100</c> (the original base).
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "ApplyBestDiscountsToRestoredProductsAsync")]
        [Trait("Scenario", "ProductWithPreviousPriceGetsBetterDiscount")]
        public async Task ApplyBestDiscounts_ProductWithPreviousPrice_GetsBetterDiscount()
        {
            // Product already has a discount: base=100, current=80
            var product = new Product { Id = 1, Price = 80m, PreviousPrice = 100m };

            // Another active discount: 30% off -> would give 70, better than 80
            var betterDiscount = new Discount
            {
                Id = 2,
                IsActive = true,
                Amount = 30m,
                IsPercentage = true,
                StartDate = DateTimeOffset.UtcNow.AddDays(-1),
                EndDate = DateTimeOffset.UtcNow.AddDays(1),
                DiscountGroup = null,
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1 } }
            };

            MockDiscountDbSet(new List<Discount> { betterDiscount });

            await _sut.ApplyBestDiscountsToRestoredProductsAsync(new List<Product> { product });

            // Base was 100, 30% off => 70
            product.Price.Should().Be(70m);
            product.PreviousPrice.Should().Be(100m); // original base preserved
        }

        #endregion

        #region RestorePricesForAffectedProductsAsync

        /// <summary>
        /// Verifies that restoring prices for a single (direct) discount correctly reverts the price to the original base
        /// and clears the <c>PreviousPrice</c> field.
        /// </summary>
        /// <remarks>
        /// Scenario: Product 1 has a current price of $80 (on sale) and a <c>PreviousPrice</c> of $100 (original).
        /// After calling <c>RestorePricesForAffectedProductsAsync</c>, the price should be set back to $100
        /// and <c>PreviousPrice</c> should be <c>null</c>, indicating the product is no longer discounted.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "RestorePricesForAffectedProductsAsync")]
        [Trait("Scenario", "SingleDiscount")]
        public async Task RestorePrices_SingleDiscount_RevertsPriceAndClearsPreviousPrice()
        {
            // Arrange
            var product = new Product { Id = 1, Price = 80m, PreviousPrice = 100m };
            var discount = new Discount
            {
                Id = 1,
                IsActive = false,
                DiscountGroup = null,
                StartDate = DateTimeOffset.UtcNow.AddDays(-2),
                EndDate = DateTimeOffset.UtcNow.AddDays(-1),
                ProductDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1, DiscountId = 1, Product = product } }
            };
            MockDbSet(new List<Product> { product });
            MockDbSet(new List<Discount> { discount });
            var productDiscounts = new List<ProductDiscount> { new ProductDiscount { ProductId = 1, DiscountId = 1, Product = product } };
            MockDbSet(productDiscounts);

            // Act
            var restored = await _sut.RestorePricesForAffectedProductsAsync(1);

            // Assert
            restored.Should().ContainSingle();
            restored[0].Price.Should().Be(100m);
            restored[0].PreviousPrice.Should().BeNull();
        }

        /// <summary>
        /// Verifies that restoring prices for a group discount correctly reverts the price to the original base
        /// and clears the <c>PreviousPrice</c> field for all affected products.
        /// </summary>
        /// <remarks>
        /// Scenario: The discount is a group discount targeting brand 5. Product 1 (brand 5) has a current price
        /// of $80 (on sale) and a <c>PreviousPrice</c> of $100. After calling <c>RestorePricesForAffectedProductsAsync</c>,
        /// the price must be set back to $100 and <c>PreviousPrice</c> must be <c>null</c>.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "RestorePricesForAffectedProductsAsync")]
        [Trait("Scenario", "GroupDiscount")]
        public async Task RestorePrices_GroupDiscount_RevertsPricesOfAffectedProducts()
        {
            var product = new Product { Id = 1, Price = 80m, PreviousPrice = 100m, ProductBrandId = 5 };
            var discount = new Discount
            {
                Id = 1,
                IsActive = false,
                StartDate = DateTimeOffset.UtcNow.AddDays(-2),
                EndDate = DateTimeOffset.UtcNow.AddDays(-1),
                DiscountGroup = new DiscountGroup
                {
                    ConditionGroups = new List<ConditionGroup>
                    {
                        new ConditionGroup
                        {
                            DiscountGroupConditions = new List<DiscountGroupCondition>
                            {
                                new DiscountGroupCondition { TargetEntity = DiscountTargetType.ProductBrand, ProductBrandId = 5 }
                            }
                        }
                    }
                }
            };

            MockDbSet(new List<Product> { product });
            MockDbSet(new List<Discount> { discount });

            var restored = await _sut.RestorePricesForAffectedProductsAsync(1);

            restored.Should().ContainSingle();
            restored[0].Price.Should().Be(100m);
            restored[0].PreviousPrice.Should().BeNull();
        }

        /// <summary>
        /// Verifies that when a product has no <c>PreviousPrice</c> (i.e., it was not discounted),
        /// it is still returned in the restored list by <c>RestorePricesForAffectedProductsAsync</c>
        /// with its <c>Price</c> unchanged and <c>PreviousPrice</c> remaining <c>null</c>.
        /// </summary>
        /// <remarks>
        /// Scenario: Product 1 (brand 1) has a price of $50 and no <c>PreviousPrice</c>.
        /// The discount is a group discount targeting brand 1.
        /// After restoration, the product must appear in the result without any modification.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "PriceService")]
        [Trait("Method", "RestorePricesForAffectedProductsAsync")]
        [Trait("Scenario", "GroupDiscountNoPreviousPrice")]
        public async Task RestorePrices_GroupDiscount_ProductWithNoPreviousPrice_StillReturned()
        {
            // Arrange
            var product = new Product
            {
                Id = 1,
                Price = 50m,
                PreviousPrice = null,
                ProductBrandId = 1
            };

            var discount = new Discount
            {
                Id = 1,
                IsActive = false,
                StartDate = DateTimeOffset.UtcNow.AddDays(-2),
                EndDate = DateTimeOffset.UtcNow.AddDays(-1),
                DiscountGroup = new DiscountGroup
                {
                    ConditionGroups = new List<ConditionGroup>
                    {
                        new ConditionGroup
                        {
                            DiscountGroupConditions = new List<DiscountGroupCondition>
                            {
                                new DiscountGroupCondition
                                {
                                    TargetEntity = DiscountTargetType.ProductBrand,
                                    ProductBrandId = 1
                                }
                            }
                        }
                    }
                }
            };

            MockDiscountDbSet(new List<Discount> { discount });
            MockDbSet(new List<Product> { product });

            // Act
            var restored = await _sut.RestorePricesForAffectedProductsAsync(1);

            // Assert
            restored.Should().ContainSingle();
            restored[0].Price.Should().Be(50m);
            restored[0].PreviousPrice.Should().BeNull();
        }

        #endregion
    }
}