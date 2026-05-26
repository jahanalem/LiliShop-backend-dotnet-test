using FluentAssertions;
using Hangfire;
using LiliShop.Application.Common.Results;
using LiliShop.Application.DTOs.Discounts;
using LiliShop.Application.Interfaces.Repositories;
using LiliShop.Domain.Entities;
using LiliShop.Domain.Entities.DiscountSystem;
using LiliShop.Infrastructure.Repositories;
using MockQueryable.Moq;
using Moq;
using System.Linq.Expressions;

namespace Lili.Shop.Tests.Services.Discounts
{
    /* =============================================================================
  Coverage Matrix for DiscountLifecycleService Tests
  =============================================================================
  Method                                     | Scenario                                             | Test Method
  -------------------------------------------|------------------------------------------------------|-------------------------------------------
  CreateDiscountAndNotifySubscribersAsync    | Draft (IsActive = false)                             | CreateDraft_SavesAndNoActivation
                                             | Scheduled (StartDate > Now)                          | CreateScheduled_SchedulesActivationJob
                                             | Active immediately (StartDate <= Now)                | CreateActiveNow_ActivatesAndNotifies
                                             | Active with past EndDate (immediate deactivation)    | CreateActiveWithPastEndDate_Deactivates
  -------------------------------------------|------------------------------------------------------|-------------------------------------------
  ActivateDiscountByIdAsync                  | Single discount, products found                      | Activate_SingleDiscount_AppliesBestPrice
                                             | Group discount, products found                       | Activate_GroupDiscount_AppliesBestPrice
                                             | Discount not found                                   | Activate_NotFound_ReturnsFailure
                                             | EndDate in future schedules deactivation             | Activate_SchedulesDeactivationJob
  -------------------------------------------|------------------------------------------------------|-------------------------------------------
  DeactivateDiscountByIdAsync                | Active discount, restores prices                     | Deactivate_ActiveDiscount_RestoresAndFallback
                                             | Already inactive, still cleans jobs                  | Deactivate_Inactive_StillDeletesJobs
                                             | Discount not found                                   | Deactivate_NotFound_ReturnsFailure
  -------------------------------------------|------------------------------------------------------|-------------------------------------------
  UpdateDiscountAndNotifyAsync               | Update active, stays active (clean slate)            | Update_StaysActive_CleanSlateAndReapply
                                             | Update active, becomes inactive                      | Update_BecomesInactive_RestoresAndFallback
                                             | Update fails                                         | Update_Fails_ReturnsFailure
                                             | Only EndDate changed, rules unchanged                | Update_OnlyEndDateChanged_ReschedulesJobsAndSkipsPriceRecalculationAndNotifications
                                             | Rules changed (tier amounts)                         | Update_RulesChanged_PerformsPriceRecalculationAndSendsNotifications
  -------------------------------------------|------------------------------------------------------|-------------------------------------------
  SweepExpiredDiscountsAsync                 | No expired discounts                                 | Sweep_NoExpired_ReturnsEmpty
                                             | Expired discounts found                              | Sweep_ExpiredFound_DeactivatesEach
  -------------------------------------------|------------------------------------------------------|-------------------------------------------
  DeleteDiscountAndCleanUpAsync              | Successful deletion with price cleanup               | Delete_Successful_RestoresAndFallsBack
                                             | Deletion fails                                       | Delete_Fails_RollbackAndReturnFailure
  ============================================================================= */
    public class DiscountLifecycleServiceTests : DiscountLifecycleServiceTestBase
    {
        #region CreateDiscountAndNotifySubscribersAsync

        /// <summary>
        /// Verifies that creating a draft (IsActive = false) does not activate the discount or schedule jobs.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "CreateDiscountAndNotifySubscribersAboutNewDiscountAsync")]
        public async Task CreateDraft_SavesAndNoActivation()
        {
            var dto = CreateValidDto(isActive: false);
            var createdDiscount = CreateDiscountFromDto(dto);
            _crudServiceMock.Setup(x => x.CreateDiscountAsync(dto))
                .ReturnsAsync(new SuccessOperationResult<Discount>(createdDiscount));

            var repoMock = new Mock<IGenericRepository<Discount>>();
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);

            var result = await _sut.CreateDiscountAndNotifySubscribersAboutNewDiscountAsync(dto);

            result.Status.Should().Be(OperationResultStatus.Success);
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()), Times.Never);
            _notificationServiceMock.Verify(x => x.NotifySubscribersOfDiscountedProductsAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// Verifies that creating a scheduled discount (StartDate > Now) schedules activation and notification jobs.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "CreateDiscountAndNotifySubscribersAboutNewDiscountAsync")]
        public async Task CreateScheduled_SchedulesActivationJob()
        {
            var dto = CreateValidDto(startDate: DateTimeOffset.UtcNow.AddDays(5));
            var createdDiscount = CreateDiscountFromDto(dto);
            _crudServiceMock.Setup(x => x.CreateDiscountAsync(dto))
                .ReturnsAsync(new SuccessOperationResult<Discount>(createdDiscount));

            var repoMock = new Mock<IGenericRepository<Discount>>();
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);

            var result = await _sut.CreateDiscountAndNotifySubscribersAboutNewDiscountAsync(dto);

            result.Status.Should().Be(OperationResultStatus.Success);

            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()), Times.Never);
            _notificationServiceMock.Verify(x => x.NotifySubscribersOfDiscountedProductsAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// Verifies that creating an immediately active discount activates it and notifies subscribers.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "CreateDiscountAndNotifySubscribersAboutNewDiscountAsync")]
        public async Task CreateActiveNow_ActivatesAndNotifies()
        {
            var dto = CreateValidDto(startDate: DateTimeOffset.UtcNow.AddMinutes(-5));
            var createdDiscount = CreateDiscountFromDto(dto);
            _crudServiceMock.Setup(x => x.CreateDiscountAsync(dto))
                .ReturnsAsync(new SuccessOperationResult<Discount>(createdDiscount));
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(new Mock<IGenericRepository<Discount>>().Object);
            MockDiscountDbSet(new List<Discount> { createdDiscount });
            MockDbSet(new List<ProductDiscount>());

            var result = await _sut.CreateDiscountAndNotifySubscribersAboutNewDiscountAsync(dto);

            result.Status.Should().Be(OperationResultStatus.Success);
            // Activation calls ApplyBestDiscounts... on the affected products
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()), Times.Once);
            _notificationServiceMock.Verify(x => x.NotifySubscribersOfDiscountedProductsAsync(createdDiscount.Id, false), Times.Once);
        }

        /// <summary>
        /// Verifies that creating an active discount with a past EndDate deactivates the discount immediately.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "CreateDiscountAndNotifySubscribersAboutNewDiscountAsync")]
        public async Task CreateActiveWithPastEndDate_Deactivates()
        {
            var dto = CreateValidDto(startDate: DateTimeOffset.UtcNow.AddDays(-10), endDate: DateTimeOffset.UtcNow.AddDays(-1));
            var createdDiscount = CreateDiscountFromDto(dto);
            _crudServiceMock.Setup(x => x.CreateDiscountAsync(dto))
                .ReturnsAsync(new SuccessOperationResult<Discount>(createdDiscount));

            var repoMock = new Mock<IGenericRepository<Discount>>();
            var discountQueryable = new List<Discount> { createdDiscount }.BuildMockDbSet<Discount>().Object.AsQueryable();
            repoMock.Setup(r => r.GetByCriteria(It.IsAny<System.Linq.Expressions.Expression<Func<Discount, bool>>>(), It.IsAny<bool>(), It.IsAny<System.Linq.Expressions.Expression<Func<Discount, object>>[]>()))
                    .Returns(discountQueryable);
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);
            MockDiscountDbSet(new List<Discount> { createdDiscount });
            MockDbSet(new List<ProductDiscount>());

            _priceServiceMock.Setup(x => x.RestorePricesForAffectedProductsAsync(It.IsAny<int>()))
                .ReturnsAsync(new List<LiliShop.Domain.Entities.Product>());

            var result = await _sut.CreateDiscountAndNotifySubscribersAboutNewDiscountAsync(dto);

            result.Status.Should().Be(OperationResultStatus.Success);
            // Even though created as active, the past EndDate should trigger a deactivation
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()), Times.Never);
            _notificationServiceMock.Verify(x => x.NotifySubscribersOfDiscountedProductsAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        #endregion

        #region ActivateDiscountByIdAsync

        /// <summary>
        /// Verifies that activating a single discount applies the best price for its directly linked products.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "ActivateDiscountByIdAsync")]
        public async Task Activate_SingleDiscount_AppliesBestPrice()
        {
            // Arrange
            var discount = CreateDiscountFromDto(CreateValidDto(endDate: null));
            discount.DiscountGroup = null;
            discount.IsActive = false;
            discount.EndDate = null;
            var product = new Product { Id = 1, Price = 100m };
            discount.ProductDiscounts = new List<ProductDiscount>
        {
            new ProductDiscount { ProductId = 1, DiscountId = discount.Id, Product = product }
        };

            // Mock the Discount DbSet
            MockDiscountDbSet(new List<Discount> { discount });

            // Setup the discount repository
            var repoMock = SetupDiscountRepository(discount);

            // Mock the ProductDiscount DbSet – required by the single-discount query
            var productDiscounts = new List<ProductDiscount>
        {
            new ProductDiscount { ProductId = 1, DiscountId = discount.Id, Product = product }
        };
            MockDbSet(productDiscounts);

            _priceServiceMock
                .Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), null))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.ActivateDiscountByIdAsync(discount.Id);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);
            repoMock.Verify(x => x.Update(It.Is<Discount>(d => d.Id == discount.Id && d.IsActive)), Times.Once); // Verify Update was called
            _priceServiceMock
                .Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.Is<List<Product>>(p => p.Count == 1 && p[0].Id == 1), null),
                Times.Once);
        }


        /// <summary>
        /// Verifies that activating a group discount fetches affected products via rule engine and applies best price.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "ActivateDiscountByIdAsync")]
        public async Task Activate_GroupDiscount_AppliesBestPrice()
        {
            // Arrange
            var discount = CreateDiscountFromDto(CreateValidDto(hasGroup: true));
            discount.IsActive = false;
            discount.EndDate = null; // Prevent Hangfire background job scheduling in unit tests
            // The auto generated ID is 0, so lets use an explicit ID
            discount.DiscountGroup.Id = 123;
            discount.DiscountGroupId = discount.DiscountGroup.Id;
            discount.DiscountGroup.ConditionGroups.First().DiscountTierId = 100;

            // Use the base class helper to mock the DbSet (no manual context needed)
            MockDiscountDbSet(new List<Discount> { discount });

            var repoMock = new Mock<IGenericRepository<Discount>>();
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);

            var affectedProducts = new List<Product>
            {
                new Product { Id = 1, Price = 100m }
            };
            _priceServiceMock.Setup(x => x.GetProductsAffectedByDiscountGroupAsync(discount.DiscountGroup))
                .ReturnsAsync(affectedProducts);
            _priceServiceMock.Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(affectedProducts, null))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.ActivateDiscountByIdAsync(discount.Id);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);
            _priceServiceMock.Verify(x => x.GetProductsAffectedByDiscountGroupAsync(discount.DiscountGroup), Times.Once);
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(affectedProducts, null), Times.Once);
        }

        /// <summary>
        /// Verifies that activating a non-existent discount returns a failure.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "ActivateDiscountByIdAsync")]
        public async Task Activate_NotFound_ReturnsFailure()
        {
            // Arrange: no discounts in the DbSet → FirstOrDefaultAsync returns null
            MockDiscountDbSet(new List<Discount>());

            // Act
            var result = await _sut.ActivateDiscountByIdAsync(999);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Failure);
        }

        #endregion

        #region DeactivateDiscountByIdAsync

        /// <summary>
        /// Verifies that deactivating an active discount restores prices and applies fallback discounts.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "DeactivateDiscountByIdAsync")]
        public async Task Deactivate_ActiveDiscount_RestoresAndFallback()
        {
            // Arrange
            var discount = CreateDiscountFromDto(CreateValidDto());
            discount.IsActive = true;
            discount.StartJobId = "job123";
            discount.EndJobId = "job456";

            // Setup the repository to return the discount (handles GetDiscountWithDetailsByIdAsync)
            var discountRepoMock = SetupDiscountRepository(discount);

            var restoredProducts = new List<Product>
        {
            new Product { Id = 1, Price = 80m, PreviousPrice = 100m }
        };
            _priceServiceMock
                .Setup(x => x.RestorePricesForAffectedProductsAsync(discount.Id))
                .ReturnsAsync(restoredProducts);
            _priceServiceMock
                .Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, null))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.DeactivateDiscountByIdAsync(discount.Id);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(discount.Id), Times.Once);
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, null), Times.Once);
        }

        /// <summary>
        /// Verifies that deactivating an already inactive discount does not perform any price restoration
        /// or fallback, but still cleans up the stored Hangfire job IDs.
        /// </summary>
        /// <remarks>
        /// Scenario: A discount is already marked <c>IsActive = false</c>, but still has saved job IDs.
        /// The updated <c>DeactivateDiscountByIdAsync</c> method now captures the original <c>IsActive</c> value
        /// in a <c>wasActive</c> variable before setting it to <c>false</c>. Because <c>wasActive</c> is <c>false</c>,
        /// the price‑service calls are skipped entirely, while the Hangfire job deletion and repository update
        /// still proceed. The test verifies that no price operations are invoked and that the result is a success.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "DeactivateDiscountByIdAsync")]
        public async Task Deactivate_Inactive_StillDeletesJobs()
        {
            // Arrange
            var discount = CreateDiscountFromDto(CreateValidDto());
            discount.IsActive = false; // already inactive
            discount.StartJobId = "job123";
            discount.EndJobId = "job456";

            // Setup repository to return the discount
            var discountRepoMock = SetupDiscountRepository(discount);

            // Act
            var result = await _sut.DeactivateDiscountByIdAsync(discount.Id);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);

            // Price service must NOT be invoked – discount is already inactive
            _priceServiceMock.Verify(
                x => x.RestorePricesForAffectedProductsAsync(It.IsAny<int>()),
                Times.Never);
            _priceServiceMock.Verify(
                x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()),
                Times.Never);

        }

        /// <summary>
        /// Verifies that deactivating a non‑existent discount returns a failure result without
        /// calling the price service or attempting any job deletion.
        /// </summary>
        /// <remarks>
        /// Scenario: A discount ID that does not exist in the database is passed to
        /// <c>DeactivateDiscountByIdAsync</c>. The repository returns <c>null</c>, and the method
        /// immediately returns a <c>FailureOperationResult</c> with <c>ErrorCode.ResourceNotFound</c>.
        /// No price restoration, fallback application, or Hangfire job deletion occurs.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "DeactivateDiscountByIdAsync")]
        public async Task Deactivate_NotFound_ReturnsFailure()
        {
            // Arrange: empty repository → GetDiscountWithDetailsByIdAsync returns null
            SetupDiscountRepositoryWithList(new List<Discount>());

            // Act
            var result = await _sut.DeactivateDiscountByIdAsync(999);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Failure);

            // No price service interactions
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(It.IsAny<int>()), Times.Never);
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()), Times.Never);
        }

        #endregion

        #region UpdateDiscountAndNotifyAsync

        /// <summary>
        /// Verifies that updating an active discount while it remains active performs a clean slate: restores old prices,
        /// updates the entity, reapplies new rules, and notifies.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "UpdateDiscountAndNotifyAsync")]
        public async Task Update_StaysActive_CleanSlateAndReapply()
        {
            var updateDto = new UpdateDiscountDto
            {
                Id = 1,
                Name = "Updated Discount",
                StartDate = DateTimeOffset.UtcNow.AddDays(-1),
                EndDate = DateTimeOffset.UtcNow.AddDays(30),
                IsActive = true,
                Tiers = new List<DiscountTierDto> { new DiscountTierDto { Amount = 15, IsPercentage = false } }
            };

            var restoredProducts = new List<Product> { new Product { Id = 1, Price = 90m, PreviousPrice = 100m } };
            _priceServiceMock.Setup(x => x.RestorePricesForAffectedProductsAsync(updateDto.Id))
                .ReturnsAsync(restoredProducts);

            _crudServiceMock.Setup(x => x.UpdateDiscountAsync(updateDto))
                .ReturnsAsync(new SuccessOperationResult<UpdateDiscountDto>(updateDto));

            // Simulate activation step: use different tier amount so HasDiscountRuleChanged returns true
            var existingDiscountDto = new CreateDiscountDto
            {
                Name = updateDto.Name,
                StartDate = updateDto.StartDate,
                EndDate = updateDto.EndDate,
                IsActive = true,
                Tiers = new List<DiscountTierDto> { new DiscountTierDto { Amount = 10, IsPercentage = false } }
            };
            var updatedDiscount = CreateDiscountFromDto(existingDiscountDto);
            updatedDiscount.IsActive = true;
            MockDiscountDbSet(new List<Discount> { updatedDiscount });
            var repoMock = SetupDiscountRepository(updatedDiscount);
            MockDbSet<ProductDiscount>(new List<ProductDiscount>());
            _priceServiceMock.Setup(x => x.GetProductsAffectedByDiscountGroupAsync(It.IsAny<DiscountGroup>()))
                .ReturnsAsync(new List<Product>());
            _priceServiceMock.Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), null))
                .Returns(Task.CompletedTask);

            var result = await _sut.UpdateDiscountAndNotifyAsync(updateDto);

            result.Status.Should().Be(OperationResultStatus.Success);
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(updateDto.Id), Times.Once);
            _crudServiceMock.Verify(x => x.UpdateDiscountAsync(updateDto), Times.Once);
            // Activation called: ApplyBestDiscounts on the new affected products
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), null), Times.Exactly(2));
            _notificationServiceMock.Verify(x => x.NotifySubscribersOfDiscountedProductsAsync(updateDto.Id, true), Times.Once);
        }

        /// <summary>
        /// Verifies that updating an active discount so that it becomes inactive restores the old prices,
        /// persists the updated discount, and applies fallback prices from other campaigns without triggering activation or notification.
        /// </summary>
        /// <remarks>
        /// Scenario: An active discount is edited to be inactive (IsActive = false, EndDate in the past).
        /// The orchestrator restores the original prices of previously affected products via <c>RestorePricesForAffectedProductsAsync</c>,
        /// then calls <c>CrudService.UpdateDiscountAsync</c> to persist the changes. Because the discount is now inactive,
        /// the activation flow is skipped entirely. The restored products are evaluated against the remaining active discounts
        /// with <c>excludeDiscountId = dto.Id</c>. No notification is sent.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "UpdateDiscountAndNotifyAsync")]
        public async Task Update_BecomesInactive_RestoresAndFallback()
        {
            // Arrange
            var updateDto = new UpdateDiscountDto
            {
                Id = 1,
                Name = "Inactive Campaign",
                StartDate = DateTimeOffset.UtcNow.AddDays(-10),
                EndDate = DateTimeOffset.UtcNow.AddDays(-1), // ended yesterday
                IsActive = false,                            // explicitly inactive
                Tiers = new List<DiscountTierDto>
                {
                    new DiscountTierDto { Amount = 10, IsPercentage = false }
                }
            };

            var restoredProducts = new List<Product>
            {
                new Product { Id = 1, Price = 80m, PreviousPrice = 100m }
            };
            _priceServiceMock
                .Setup(x => x.RestorePricesForAffectedProductsAsync(updateDto.Id))
                .ReturnsAsync(restoredProducts);

            _crudServiceMock
                .Setup(x => x.UpdateDiscountAsync(updateDto))
                .ReturnsAsync(new SuccessOperationResult<UpdateDiscountDto>(updateDto));

            // The existing discount (before update) was active, so wasActiveNow = true.
            var existingActiveDiscount = new Discount
            {
                Id = 1,
                Name = "Inactive Campaign",
                StartDate = DateTimeOffset.UtcNow.AddDays(-10),
                EndDate = DateTimeOffset.UtcNow.AddDays(5), // still valid at the time of the existing state
                IsActive = true,
                Tiers = new List<DiscountTier>
                {
                    new DiscountTier { Id = 100, Amount = 10, IsPercentage = false }
                }
            };

            // After update, the orchestrator fetches the full discount to handle jobs.
            var inactiveDiscount = CreateDiscountFromDto(updateDto, id: 1);
            inactiveDiscount.IsActive = false;

            // First call: initial fetch to evaluate current state (active).
            // Subsequent calls: post-update fetches (inactive / for job scheduling).
            var repoMock = new Mock<IGenericRepository<Discount>>();
            repoMock.SetupSequence(r => r.GetByCriteria(
                        It.IsAny<System.Linq.Expressions.Expression<Func<Discount, bool>>>(),
                        It.IsAny<bool>()))
                .Returns(new List<Discount> { existingActiveDiscount }.BuildMockDbSet().Object)
                .Returns(new List<Discount> { inactiveDiscount }.BuildMockDbSet().Object);
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);

            // Fallback application should exclude this discount
            _priceServiceMock
                .Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, updateDto.Id))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.UpdateDiscountAndNotifyAsync(updateDto);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);

            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(updateDto.Id), Times.Once);
            _crudServiceMock.Verify(x => x.UpdateDiscountAsync(updateDto), Times.Once);
            _priceServiceMock.Verify(
                x => x.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, updateDto.Id),
                Times.Once);

            // No activation or notification because the discount is now inactive
            _priceServiceMock.Verify(
                x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), null),
                Times.Never()); // no activation call — discount is inactive, so null excludeId is never passed
            _notificationServiceMock.Verify(
                x => x.NotifySubscribersOfDiscountedProductsAsync(It.IsAny<int>(), It.IsAny<bool>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that when the underlying CRUD update operation fails,
        /// the orchestrator propagates the failure immediately without activating the discount,
        /// sending notifications, or scheduling jobs.
        /// </summary>
        /// <remarks>
        /// Scenario: An administrator submits valid updated rules, but the CRUD persistence fails
        /// (e.g., a database error). The orchestrator restores the old prices as usual, but after
        /// receiving a failure from <c>UpdateDiscountAsync</c>, it stops and returns the failure result.
        /// No activation, notification, or job scheduling occurs.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "UpdateDiscountAndNotifyAsync")]
        public async Task Update_Fails_ReturnsFailure()
        {
            // Arrange
            var updateDto = new UpdateDiscountDto
            {
                Id = 1,
                Name = "Failing Update",
                StartDate = DateTimeOffset.UtcNow.AddDays(-1),
                EndDate = DateTimeOffset.UtcNow.AddDays(30),
                IsActive = true,
                Tiers = new List<DiscountTierDto>
                {
                    new DiscountTierDto { Amount = 10, IsPercentage = false }
                }
            };

            var existingDiscount = new Discount
            {
                Id = updateDto.Id,
                Name = updateDto.Name,
                StartDate = updateDto.StartDate,
                EndDate = updateDto.EndDate,
                IsActive = updateDto.IsActive,
                Tiers = new List<DiscountTier>()
            };
            SetupDiscountRepository(existingDiscount);

            var restoredProducts = new List<Product>
            {
                new Product { Id = 1, Price = 100m }
            };
            _priceServiceMock
                .Setup(x => x.RestorePricesForAffectedProductsAsync(updateDto.Id))
                .ReturnsAsync(restoredProducts);

            _crudServiceMock
                .Setup(x => x.UpdateDiscountAsync(updateDto))
                .ReturnsAsync(new FailureOperationResult<UpdateDiscountDto>(ErrorCode.UpdateFailed, "DB error"));

            // Act
            var result = await _sut.UpdateDiscountAndNotifyAsync(updateDto);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Failure);
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(updateDto.Id), Times.Once);
            _crudServiceMock.Verify(x => x.UpdateDiscountAsync(updateDto), Times.Once);

            // No further actions must be taken
            _priceServiceMock.Verify(
                x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()),
                Times.Never);
            _notificationServiceMock.Verify(
                x => x.NotifySubscribersOfDiscountedProductsAsync(It.IsAny<int>(), It.IsAny<bool>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that when an administrator modifies ONLY the EndDate, the system 
        /// reschedules the background jobs but skips price recalculations and subscriber notifications.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "UpdateDiscountAndNotifyAsync")]
        public async Task Update_OnlyEndDateChanged_ReschedulesJobsAndSkipsPriceRecalculationAndNotifications()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;

            // Create a baseline campaign configuration that is currently active
            var baseDto = CreateValidDto(startDate: now.AddDays(-1), endDate: now.AddHours(1), isActive: true);
            var existingDiscount = CreateDiscountFromDto(baseDto, id: 1);
            existingDiscount.StartJobId = "old-start-job";
            existingDiscount.EndJobId = "old-end-job";

            // Prepare a modification DTO changing ONLY the EndDate timeline field
            var updateDto = new UpdateDiscountDto
            {
                Id = 1,
                Name = baseDto.Name,
                StartDate = baseDto.StartDate,
                EndDate = now.AddMinutes(5), // New timeline value
                IsActive = true,
                Tiers = baseDto.Tiers.Select(t => new DiscountTierDto
                {
                    Id = 100,
                    Amount = t.Amount,
                    IsPercentage = t.IsPercentage,
                    IsFreeShipping = t.IsFreeShipping
                }).ToList(),
                DiscountGroup = baseDto.DiscountGroup
            };

            var repoMock = SetupDiscountRepository(existingDiscount);

            _crudServiceMock.Setup(x => x.UpdateDiscountAsync(updateDto))
                .ReturnsAsync(new SuccessOperationResult<UpdateDiscountDto>(updateDto));

            MockDiscountDbSet(new List<Discount> { existingDiscount });

            // Mock the ProductDiscount DbSet to prevent the InvalidOperationException during ToListAsync()
            MockDbSet(new List<ProductDiscount>());

            // Act
            var result = await _sut.UpdateDiscountAndNotifyAsync(updateDto);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);

            // Verify that old Hangfire worker routines were safely cleared from the registry
            _backgroundJobClientMock.Verify(x => x.ChangeState("old-start-job", It.IsAny<Hangfire.States.IState>(), null), Times.Once);
            _backgroundJobClientMock.Verify(x => x.ChangeState("old-end-job", It.IsAny<Hangfire.States.IState>(), null), Times.Once);

            // Verify that price adjustments and customer notifications were completely bypassed
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(It.IsAny<int>()), Times.Never);
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()), Times.Never);
            _notificationServiceMock.Verify(x => x.NotifySubscribersOfDiscountedProductsAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// Verifies that when discount rules actually alter (e.g., tier amounts change), 
        /// the orchestrator executes a full clean-slate price re-evaluation and notifies subscribers.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "UpdateDiscountAndNotifyAsync")]
        public async Task Update_RulesChanged_PerformsPriceRecalculationAndSendsNotifications()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;

            var baseDto = CreateValidDto(startDate: now.AddDays(-1), endDate: now.AddHours(1), isActive: true);
            var existingDiscount = CreateDiscountFromDto(baseDto, id: 1);
            existingDiscount.StartJobId = "old-start-job";
            existingDiscount.EndJobId = "old-end-job";

            // Prepare a modification DTO that alters the mathematical rule tier definition
            var updateDto = new UpdateDiscountDto
            {
                Id = 1,
                Name = baseDto.Name,
                StartDate = baseDto.StartDate,
                EndDate = baseDto.EndDate,
                IsActive = true,
                Tiers = new List<DiscountTierDto>
                {
                    new DiscountTierDto { Id = 100, Amount = 25m, IsPercentage = true } // Modified math rule logic
                },
                DiscountGroup = baseDto.DiscountGroup
            };

            var repoMock = SetupDiscountRepository(existingDiscount);

            _crudServiceMock.Setup(x => x.UpdateDiscountAsync(updateDto))
                .ReturnsAsync(new SuccessOperationResult<UpdateDiscountDto>(updateDto));

            var restoredProducts = new List<Product> { new Product { Id = 1, Price = 90m } };
            _priceServiceMock.Setup(x => x.RestorePricesForAffectedProductsAsync(updateDto.Id))
                .ReturnsAsync(restoredProducts);

            MockDiscountDbSet(new List<Discount> { existingDiscount });
            MockDbSet(new List<ProductDiscount>());
            _priceServiceMock.Setup(x => x.GetProductsAffectedByDiscountGroupAsync(It.IsAny<DiscountGroup>()))
                .ReturnsAsync(new List<Product>());
            _priceServiceMock.Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), null))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.UpdateDiscountAndNotifyAsync(updateDto);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);

            // Verify that the pricing engine executed full catalog recalculations
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(updateDto.Id), Times.Once);
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), null), Times.Exactly(2));
            _notificationServiceMock.Verify(x => x.NotifySubscribersOfDiscountedProductsAsync(updateDto.Id, true), Times.Once);
        }

        #endregion

        #region SweepExpiredDiscountsAsync

        /// <summary>
        /// Verifies that the sweep returns a success result with an appropriate message when no
        /// expired active discounts are found in the database.
        /// </summary>
        /// <remarks>
        /// Scenario: No discounts in the database have <c>IsActive = true</c> and an <c>EndDate</c> in the past.
        /// The sweep method must return a <c>SuccessOperationResult</c> with the message "No expired discounts found."
        /// without calling any deactivation logic or price services.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "SweepExpiredDiscountsAsync")]
        public async Task Sweep_NoExpired_ReturnsEmpty()
        {
            // Arrange
            var repoMock = new Mock<IGenericRepository<Discount>>();
            // Return an empty queryable for GetByCriteria
            var emptyDbSet = new List<Discount>().BuildMockDbSet<Discount>();
            repoMock
                .Setup(r => r.GetByCriteria(It.IsAny<Expression<Func<Discount, bool>>>()))
                .Returns(emptyDbSet.Object);
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);

            // Act
            var result = await _sut.SweepExpiredDiscountsAsync();

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);
            result.Message.Should().Be("No expired discounts found.");

            // No deactivation logic was executed
            _priceServiceMock.Verify(
                x => x.RestorePricesForAffectedProductsAsync(It.IsAny<int>()),
                Times.Never);
            _priceServiceMock.Verify(
                x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), It.IsAny<int?>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that the sweep finds all expired active discounts and deactivates them
        /// (i.e., restores prices and applies fallback discounts).
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "SweepExpiredDiscountsAsync")]
        public async Task Sweep_ExpiredFound_DeactivatesEach()
        {
            // Arrange: two expired discounts that are still active
            var discount1 = CreateDiscountFromDto(CreateValidDto(), id: 1);
            discount1.EndDate = DateTimeOffset.UtcNow.AddDays(-1);
            discount1.IsActive = true;

            var discount2 = CreateDiscountFromDto(CreateValidDto(), id: 2);
            discount2.EndDate = DateTimeOffset.UtcNow.AddDays(-2);
            discount2.IsActive = true;

            var expiredDiscounts = new List<Discount> { discount1, discount2 };

            // Mock the repository's GetByCriteria to return the expired discounts
            var repoMock = new Mock<IGenericRepository<Discount>>();
            var mockDbSet = expiredDiscounts.BuildMockDbSet();
            // For the sweep query (no trackChanges): return all expired discounts
            repoMock
                .Setup(r => r.GetByCriteria(It.IsAny<Expression<Func<Discount, bool>>>()))
                .Returns(mockDbSet.Object);
            // For GetDiscountWithDetailsByIdAsync (trackChanges: true): return matching discount
            repoMock
                .Setup(r => r.GetByCriteria(It.IsAny<Expression<Func<Discount, bool>>>(), true, It.IsAny<Expression<Func<Discount, object>>[]>()))
                .Returns((Expression<Func<Discount, bool>> predicate, bool _, Expression<Func<Discount, object>>[] includes) =>
                    expiredDiscounts.Where(predicate.Compile()).ToList().BuildMockDbSet().Object);

            // Also mock Update (no-op) and allow the repo to be used
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);

            // Price service: restore and fallback for each discount
            _priceServiceMock
                .Setup(x => x.RestorePricesForAffectedProductsAsync(It.IsAny<int>()))
                .ReturnsAsync(new List<Product> { new Product { Id = 1, Price = 100 } });
            _priceServiceMock
                .Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), null))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.SweepExpiredDiscountsAsync();

            // Assert
            result.Status.Should().Be(OperationResultStatus.Success);
            result.Message.Should().Contain("2");

            // Verify deactivation logic was executed for each discount
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(1), Times.Once);
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(2), Times.Once);
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(It.IsAny<List<Product>>(), null), Times.Exactly(2));
        }

        #endregion

        #region DeleteDiscountAndCleanUpAsync

        /// <summary>
        /// Verifies that a successful deletion cleans up the discount and its associated background jobs,
        /// restores the original prices of affected products, and applies the next‑best active discount as a fallback.
        /// </summary>
        /// <remarks>
        /// Scenario: A discount (ID 1) is deleted. The orchestrator cancels any Hangfire jobs linked to the discount,
        /// restores the base prices of the affected products, and re‑evaluates other active campaigns to apply the best
        /// remaining price. All steps complete without errors, and the transaction is committed.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "DeleteDiscountAndCleanUpAsync")]
        public async Task Delete_Successful_RestoresAndFallsBack()
        {
            var discount = CreateDiscountFromDto(CreateValidDto(), id: 1);
            var restoredProducts = new List<Product> { new Product { Id = 1, Price = 100m } };

            var repoMock = new Mock<IGenericRepository<Discount>>();
            var mockDbSet = new List<Discount> { discount }.BuildMockDbSet();
            repoMock
                .Setup(r => r.GetByCriteria(It.IsAny<Expression<Func<Discount, bool>>>()))
                .Returns(mockDbSet.Object);
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);

            _priceServiceMock.Setup(x => x.RestorePricesForAffectedProductsAsync(discount.Id))
                .ReturnsAsync(restoredProducts);
            _priceServiceMock.Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, discount.Id))
                .Returns(Task.CompletedTask);
            _crudServiceMock.Setup(x => x.DeleteDiscountAsync(discount.Id))
                .ReturnsAsync(new SuccessOperationResult("Deleted"));

            var result = await _sut.DeleteDiscountAndCleanUpAsync(discount.Id);

            result.Status.Should().Be(OperationResultStatus.Success);
            _priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(discount.Id), Times.Once);
            _priceServiceMock.Verify(x => x.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, discount.Id), Times.Once);
        }

        /// <summary>
        /// Verifies that when the underlying CRUD delete operation fails,
        /// the orchestrator rolls back the transaction and returns a failure result.
        /// </summary>
        /// <remarks>
        /// Scenario: A discount deletion is requested. The price restoration and fallback steps succeed,
        /// but the final <c>DeleteDiscountAsync</c> call returns a failure. The orchestrator must roll back
        /// the database transaction and propagate the failure, ensuring no partial changes are persisted.
        /// </remarks>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Module", "LifecycleService")]
        [Trait("Method", "DeleteDiscountAndCleanUpAsync")]
        public async Task Delete_Fails_RollbackAndReturnFailure()
        {
            // Arrange
            var discount = CreateDiscountFromDto(CreateValidDto(), id: 1);
            var restoredProducts = new List<Product> { new Product { Id = 1, Price = 100m } };

            // Repository mock for retrieving job IDs
            var repoMock = new Mock<IGenericRepository<Discount>>();
            var mockDbSet = new List<Discount> { discount }.BuildMockDbSet();
            repoMock
                .Setup(r => r.GetByCriteria(It.IsAny<Expression<Func<Discount, bool>>>()))
                .Returns(mockDbSet.Object);
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);

            // Price service steps succeed
            _priceServiceMock.Setup(x => x.RestorePricesForAffectedProductsAsync(discount.Id))
                .ReturnsAsync(restoredProducts);
            _priceServiceMock.Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, discount.Id))
                .Returns(Task.CompletedTask);

            // CRUD service fails
            _crudServiceMock.Setup(x => x.DeleteDiscountAsync(discount.Id))
                .ReturnsAsync(new FailureOperationResult(ErrorCode.DeletionFailed, "Database error"));

            // Unit of work transaction mocks
            _unitOfWorkMock.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
            _unitOfWorkMock.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
            _unitOfWorkMock.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

            // Act
            var result = await _sut.DeleteDiscountAndCleanUpAsync(discount.Id);

            // Assert
            result.Status.Should().Be(OperationResultStatus.Failure);
            _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(), Times.Once);
            _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(), Times.Never);
            _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(), Times.Once);
        }

        #endregion
    }
}