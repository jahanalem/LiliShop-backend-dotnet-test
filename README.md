# Discount System Unit Tests – Technical Documentation

**Version:** 1.0  
**Project:** LiliShop – Discount System – Unit Tests  
**Date:** 2026-05-23  

---

## 1. Introduction

The refactored discount system splits the original “God Class” `DiscountService` into four focused services. Each service has a single, clear job. The unit test suite mirrors this separation. It targets the two services that hold the most critical business logic:

- **`DiscountPriceService`** – Pure domain logic: tier resolution, price calculation, and the best‑price engine.
- **`DiscountLifecycleService`** – The orchestrator: activation, deactivation, updates, sweeping, and deletion workflows.

The CRUD and query services are not covered here. They are thin persistence and read layers that will be tested with integration tests against a real database.

This document describes what the tests cover, how they are organized, why specific testing choices were made, and how to extend the suite when new features are added.

### What This Document Covers

- **Test Organization** – Base classes, mocking strategy, and naming conventions.
- **Coverage Matrix – PriceService** – Every method and scenario tested in `DiscountPriceService`.
- **Coverage Matrix – LifecycleService** – Every method and workflow tested in `DiscountLifecycleService`.
- **Key Testing Patterns** – How we mock the database, how we simulate concurrency, and how we verify orchestrator behaviour.
- **How to Extend** – Where to add new tests and what conventions to follow.

### Table of Contents

- [Test Organization and Philosophy](#test-organization-and-philosophy)
- [Coverage Matrix – DiscountPriceService](#coverage-matrix--discountpriceservice)
- [Coverage Matrix – DiscountLifecycleService](#coverage-matrix--discountlifecycleservice)
- [Mocking Strategy and Helpers](#mocking-strategy-and-helpers)
- [Naming Conventions and Traits](#naming-conventions-and-traits)
- [How the Tests Map to the Production Architecture](#how-the-tests-map-to-the-production-architecture)
- [Conclusion](#conclusion)

### Visual Overview of the Test Suite

The two screenshots below show the test structure as it appears in Visual Studio. The first captures the test methods for the `DiscountPriceService`; the second captures the test methods for the `DiscountLifecycleService`. In both images, the **Test Explorer** window on the left confirms that every test passes, and the code editor on the right shows the corresponding test class with its trait attributes and fluent assertions.

**Screenshot 1 – Price Service Test Coverage**  
<img width="2958" height="1530" alt="Coverage-Matrix-for-DiscountPriceService-Tests" src="https://github.com/user-attachments/assets/2f9853fc-58aa-4b88-83c8-e9724bdd6f1a" />

*All 25 tests for the `DiscountPriceService` are listed in Test Explorer, grouped by method (`CalculateEffectivePrice`, `ResolveTierForProduct`, etc.). The editor pane displays the test class with the coverage matrix comment at the top, followed by the `CalculateEffectivePrice` tests that verify the base‑price rule, percentage and fixed‑amount calculations, and safe fallbacks for null values.*

**Screenshot 2 – Lifecycle Service Test Coverage**  
<img width="2938" height="1432" alt="Coverage-Matrix-for-DiscountLifecycleService-Tests" src="https://github.com/user-attachments/assets/f045415e-97e2-42e6-83ab-968c5394f8a9" />

*All 17 tests for the `DiscountLifecycleService` are visible. The editor shows the `Update_StaysActive_CleanSlateAndReapply` test as an example, demonstrating how the orchestrator’s behaviour is verified: mocks are set up, the update method is called, and then assertions confirm that price restoration, CRUD updates, best‑price application, and notifications all happened in the correct order.*

These screenshots provide a real‑world snapshot of the test environment. The full coverage matrices in Sections 3 and 4 map every test you see here to the business rule it validates.

---
## 2. Test Organization and Philosophy

### 2.1 Testing Tools Used

We use a small set of libraries to keep the tests fast, readable, and easy to maintain:

| Tool | What It Does |
|------|--------------|
| **xUnit** | The test framework that runs the tests and reports results. |
| **Moq** | Creates fake (mock) versions of services, the database context, and background job clients, so tests don't need real infrastructure. |
| **FluentAssertions** | Provides a readable, fluent syntax for assertions (e.g., `result.Should().Be(80m)`). |
| **MockQueryable.Moq** | Turns a plain `List<T>` into a mocked Entity Framework `DbSet<T>` that supports LINQ queries. This lets us test database logic entirely in memory. |

These four tools together mean that all 42 unit tests run in under 5 seconds without a database, without Hangfire, and without any network calls.

### 2.2 The Split: Two Services, Two Test Classes

| Test Class | Production Service Under Test | Number of Tests |
|---|---|---|
| `DiscountPriceServiceTests` | `DiscountPriceService` | 25 |
| `DiscountLifecycleServiceTests` | `DiscountLifecycleService` | 17 |

The `DiscountPriceService` tests focus on **pure logic** – given a product and a discount, what price should the customer see? These tests run fast. They mock the database and never touch a real SQL server.

The `DiscountLifecycleService` tests focus on **orchestration** – when an admin clicks “Save,” does the service call the right methods in the right order? These tests also run fast, because they mock all three sibling services (`CrudService`, `QueryService`, `PriceService`) plus Hangfire and the cache manager.

### 2.3 Base Classes Eliminate Repetition

Both test classes inherit from an abstract base class that sets up all shared mocks:

```
DiscountPriceServiceTestBase
        ▲
        |
DiscountPriceServiceTests
```

```
DiscountLifecycleServiceTestBase
        ▲
        |
DiscountLifecycleServiceTests
```

The base classes:

- Create the **System Under Test** (`_sut`) so every test starts with a fresh, correctly mocked service.
- Provide **helper methods** like `MockDiscountDbSet` and `MockDbSet<T>` that turn a plain `List<T>` into a mocked Entity Framework `DbSet<T>`.
- In `DiscountLifecycleServiceTestBase`, also provide `CreateValidDto` and `CreateDiscountFromDto` to build realistic test data with minimal code.

This means a typical test method is **only 8 to 15 lines long**. The setup is minimal, and the assertion is clear.

### 2.4 What These Tests Are NOT

These tests are **unit tests**, not integration tests. They:

- Do **not** connect to a real database.
- Do **not** test Entity Framework mappings or SQL queries.
- Do **not** test Hangfire job execution (the jobs are mocked).
- Do **not** test the `DiscountCrudService` or `DiscountQueryService` (those will be covered separately).

They verify that the **business logic** inside each service is correct, assuming all dependencies work as expected.

---
## 3. Coverage Matrix – DiscountPriceService

The `DiscountPriceService` contains the most delicate logic in the entire discount engine: how to match a discount to a product, how to compute the new price, and how to pick the lowest price when multiple discounts overlap.

### 3.1 Key Concepts Proven by the PriceService Tests

Before diving into the individual test methods, here is a summary of the important business rules that these tests verify:

| Concept | What The Tests Prove |
|---------|----------------------|
| **Base Price Rule** | Discounts are always calculated from `PreviousPrice` if the product is already on sale. Otherwise, from the current `Price`. |
| **Overlapping Discounts** | When a product qualifies for multiple group discounts, the system picks the lowest calculated price. |
| **Priority Rule** | A direct (single) discount assigned to a product always overrides any group discount, even if the group discount would be cheaper. |
| **Target Matching** | A group discount only applies when **all** conditions in a condition group are met (AND logic). Matching works for Brand, Type, Size, and the special `All` target. |
| **Exclusion Rule** | When an `excludeDiscountId` is provided, that discount is completely ignored—even if it would give the best price. |
| **Safe Fallbacks** | Null tiers, null amounts, zero discounts, and empty condition groups are handled gracefully without errors. |

This table gives you a quick, high‑level view of what the price service guarantees. The coverage matrix below maps each of these concepts to the specific test methods that enforce them.

### 3.2 `CalculateEffectivePrice` – 6 Tests

This method takes a product and either a `DiscountTier` or raw `amount`/`isPercentage` values, and returns the effective price. It always uses the base price (`PreviousPrice ?? Price`).

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `WithFixedAmountAndPreviousPrice_...` | Product is $90 (base $100). Apply $15 flat discount. | Result = $85 (calculated from base $100). |
| 2 | `WithPercentage_ReturnsCorrectPrice` | Product is $100. Apply 20% off. | Result = $80. |
| 3 | `WithFixedAmount_ReturnsCorrectPrice` | Product is $100. Apply $15 off. | Result = $85. |
| 4 | `WithPreviousPrice_CalculatesFromPreviousPrice` | Product is $80 (base $100). Apply 50% off. | Result = $50 (from base $100). |
| 5 | `WithZeroDiscount_ReturnsUnchangedPrice` | Product is $150. Apply $0 off. | Result = $150 (no change). |
| 6 | `WithTier_NullTier_ReturnsProductPrice` | Null tier passed. | Returns current price without error. |

### 3.3 `ResolveTierForProduct` – 7 Tests

This method walks through a discount’s condition groups and finds the first `DiscountTier` whose conditions all match the product. If no group matches, it returns `null`.

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `WithSingleDiscount_ReturnsNull` | Discount has no `DiscountGroup`. | Single discounts don't use tiers. |
| 2 | `WithGroupTargetAll_ReturnsTier` | Group targets `All`. | Any product matches; tier returned. |
| 3 | `WithGroupTargetBrand_MatchesCorrectBrand` | Group targets brand 5. | Product with brand 5 matches; brand 9 does not. |
| 4 | `WithSizeClassification_MatchesCorrectSize` | Group targets size 3. | Product with characteristic size 3 matches; size 4 does not. |
| 5 | `WithProductType_MatchesCorrectType` | Group targets type 10. | Product with type 10 matches; type 99 does not. |
| 6 | `WithMultipleConditions_AllMustMatch` | Group has brand=5 AND type=10. | Both conditions must be true for the tier to resolve. |
| 7 | `WithDiscountGroupButNoConditionGroups_ReturnsNull` | `ConditionGroups` is `null`. | Safe fallback; returns `null`. |

### 3.4 `ApplyBestDiscountsToRestoredProductsAsync` – 8 Tests

This is the **best‑price engine**. It takes a list of products, loads all active discounts, calculates the lowest possible price for each product, and updates the database. It respects an optional `excludeDiscountId` parameter and handles concurrency conflicts with a retry loop.

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `WithExcludedSingleDiscount_IgnoresExcludedDiscount` | Excluded discount would give 50% off. | Product stays at $100; exclusion works. |
| 2 | `SingleAlwaysOverridesGroup_EvenIfGroupIsCheaper` | Single offers $90; group offers $50. | Single wins ($90), not group. |
| 3 | `MultipleGroupOverlaps_PicksLowestPrice` | Group1: $90, Group2: $75. | $75 is selected (lowest). |
| 4 | `IgnoresInactiveAndExpiredDiscounts` | Expired, future, and inactive discounts. | None applied; product stays at $100. |
| 5 | `WithExcludedDiscountId_IgnoresExcludedId` | Direct ID exclusion. | Excluded discount ignored. |
| 6 | `ProcessesMultipleProductsCorrectly` | Three products: group, single, none. | Each product gets correct price. |
| 7 | `EmptyList_ReturnsWithoutError` | Empty product list. | No exception thrown. |
| 8 | `ProductWithPreviousPrice_GetsBetterDiscount` | Product $80 (base $100), new 30% off. | New price = $70 (from base). |

### 3.5 `RestorePricesForAffectedProductsAsync` – 3 Tests

This method undoes a discount. It finds all products affected by a discount, sets `Price = PreviousPrice`, and clears `PreviousPrice`.

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `SingleDiscount_RevertsPriceAndClearsPreviousPrice` | Product is $80 (base $100). | After restore: Price = $100, PreviousPrice = null. |
| 2 | `GroupDiscount_RevertsPricesOfAffectedProducts` | Group discount on brand 5. | Same revert logic works for group discounts. |
| 3 | `GroupDiscount_ProductWithNoPreviousPrice_StillReturned` | Product was never discounted. | Still appears in result list; price unchanged. |

---
## 4. Coverage Matrix – DiscountLifecycleService

The `DiscountLifecycleService` orchestrates all discount state transitions. It coordinates the three sibling services (`CrudService`, `QueryService`, `PriceService`), Hangfire, the cache, and notifications.

### 4.1 Key Concepts Proven by the LifecycleService Tests

The lifecycle orchestrator is all about coordination. These tests verify that the right things happen in the right order. Here are the high‑level behaviours the tests prove:

| Concept | What The Tests Prove |
|---------|----------------------|
| **Job Scheduling** | Hangfire jobs are created for future activation and deactivation, deleted when a discount is deactivated, and rescheduled when dates change. No stale jobs are left behind. |
| **Clean Slate Updates** | When an active campaign is modified, the system always restores old prices first, then saves the new rules, and then recalculates prices. The old discount never leaks into the new reality. |
| **Transaction Safety** | If saving to the database fails, the entire operation rolls back. No partial data is saved, and no inconsistent state is left in the database. |
| **The Sweeper Failsafe** | The recurring `SweepExpiredDiscountsAsync` job correctly finds active discounts whose end date has passed and deactivates them, using the exact same deactivation logic as the normal scheduled job. |
| **Notification Triggers** | Notifications are sent only when a discount becomes active right now. Drafts, scheduled discounts, and deactivations do not trigger notifications. |

These concepts are proven by the 17 tests described in detail below.

### 4.2 `CreateDiscountAndNotifySubscribersAboutNewDiscountAsync` – 4 Tests

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `CreateDraft_SavesAndNoActivation` | `IsActive = false`. | No activation or notification occurs. |
| 2 | `CreateScheduled_SchedulesActivationJob` | `StartDate` in 5 days. | Jobs scheduled; no immediate price changes. |
| 3 | `CreateActiveNow_ActivatesAndNotifies` | `StartDate` in the past. | Prices applied, notifications sent. |
| 4 | `CreateActiveWithPastEndDate_Deactivates` | `EndDate` already past. | Immediate deactivation, no notification. |

### 4.3 `ActivateDiscountByIdAsync` – 3 Tests

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `Activate_SingleDiscount_AppliesBestPrice` | Single discount with direct product links. | `ApplyBestDiscounts` called with correct products. |
| 2 | `Activate_GroupDiscount_AppliesBestPrice` | Group discount with rule engine. | Affected products fetched by rule, then best price applied. |
| 3 | `Activate_NotFound_ReturnsFailure` | Discount ID doesn't exist. | Returns failure result; no price changes. |

### 4.4 `DeactivateDiscountByIdAsync` – 3 Tests

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `Deactivate_ActiveDiscount_RestoresAndFallback` | Active discount deactivated. | Prices restored, fallback applied. |
| 2 | `Deactivate_Inactive_StillDeletesJobs` | Already inactive. | Jobs cleaned up; no price changes. |
| 3 | `Deactivate_NotFound_ReturnsFailure` | Discount doesn't exist. | Returns failure; no price changes. |

### 4.5 `UpdateDiscountAndNotifyAsync` – 3 Tests

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `Update_StaysActive_CleanSlateAndReapply` | Active discount updated, stays active. | Restore → Update → Reactivate → Notify. |
| 2 | `Update_BecomesInactive_RestoresAndFallback` | Active discount becomes inactive. | Restore → Update → Fallback to other discounts. No activation. |
| 3 | `Update_Fails_ReturnsFailure` | CRUD update fails. | Restore happens, then failure propagated. No activation or notification. |

### 4.6 `SweepExpiredDiscountsAsync` – 2 Tests

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `Sweep_NoExpired_ReturnsEmpty` | No expired active discounts. | Returns "No expired discounts found." |
| 2 | `Sweep_ExpiredFound_DeactivatesEach` | Two expired discounts. | Both deactivated via `DeactivateDiscountByIdAsync`. |

### 4.7 `DeleteDiscountAndCleanUpAsync` – 2 Tests

| # | Test | Scenario | What It Verifies |
|---|---|---|---|
| 1 | `Delete_Successful_RestoresAndFallsBack` | Active discount deleted. | Prices restored, fallback applied, transaction committed. |
| 2 | `Delete_Fails_RollbackAndReturnFailure` | CRUD delete fails. | Transaction rolled back; failure returned. |

---

## 5. Mocking Strategy and Helpers

### 5.1 Database Mocking Without a Real Database

Both test suites use the NuGet package **`MockQueryable.Moq`**. It lets us turn any `List<T>` into a mocked `DbSet<T>` that supports `IQueryable` operations.

```csharp
protected void MockDbSet<TEntity>(List<TEntity> data) where TEntity : class
{
    var mockSet = data.BuildMockDbSet<TEntity>();
    _dbContextMock.Setup(c => c.Set<TEntity>()).Returns(mockSet.Object);
}
```

This means we can write:

```csharp
MockDiscountDbSet(new List<Discount> { discount1, discount2 });
```

And the service will “see” those two discounts when it queries `_unitOfWork.Context.Set<Discount>()`. This is the foundation that makes all the unit tests fast and deterministic.

### 5.2 Lifecycle Service – Mocking the Sibling Services

The `DiscountLifecycleService` depends on three other services. The test base creates **strict mocks** for each:

| Mock | Purpose |
|---|---|
| `_crudServiceMock` | Simulates `CreateDiscountAsync`, `UpdateDiscountAsync`, `DeleteDiscountAsync`. |
| `_queryServiceMock` | Not directly used in these tests (the lifecycle service accesses the database directly for some operations). |
| `_priceServiceMock` | Simulates `RestorePricesForAffectedProductsAsync`, `ApplyBestDiscountsToRestoredProductsAsync`, `GetProductsAffectedByDiscountGroupAsync`. |

Each test **sets up** only the mocks it needs. For example, a deactivation test will set up:

```csharp
_priceServiceMock.Setup(x => x.RestorePricesForAffectedProductsAsync(discount.Id))
    .ReturnsAsync(restoredProducts);
_priceServiceMock.Setup(x => x.ApplyBestDiscountsToRestoredProductsAsync(restoredProducts, null))
    .Returns(Task.CompletedTask);
```

Then the test **verifies** that these methods were called with the right arguments:

```csharp
_priceServiceMock.Verify(x => x.RestorePricesForAffectedProductsAsync(discount.Id), Times.Once);
```

This “Arrange → Act → Assert → Verify” pattern gives us confidence that the orchestrator is wiring everything together correctly.

### 5.3 Hangfire Mocking

The `IBackgroundJobClient` is mocked so that no real Hangfire jobs are created or deleted during tests. The mock is configured to return a fake job ID (`"test-job-id"`) from `Create`, and all other methods (`Delete`, `Schedule`) are no‑ops.

```csharp
_backgroundJobClientMock
    .Setup(c => c.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()))
    .Returns("test-job-id");
```

### 5.4 Test Data Factories

The `DiscountLifecycleServiceTestBase` class provides two helpers that make writing tests quick:

- **`CreateValidDto`** – Builds a `CreateDiscountDto` with sensible defaults. The caller can override any field (name, dates, `IsActive`, `hasGroup`) to match the specific scenario.
- **`CreateDiscountFromDto`** – Takes a DTO and builds the corresponding `Discount` entity with tiers, groups, and conditions fully populated. This mirrors what the real `CrudService` would produce, so the lifecycle service can consume it directly.

These helpers mean a test can set up a complete discount in **one or two lines**:

```csharp
var dto = CreateValidDto(startDate: DateTimeOffset.UtcNow.AddDays(5));
var createdDiscount = CreateDiscountFromDto(dto);
```

---

## 6. Naming Conventions and Traits

### 6.1 Method Naming

The tests follow the pattern:

```
{MethodUnderTest}_{Scenario}_{ExpectedBehavior}
```

For example:

- `CalculateEffectivePrice_WithFixedAmount_ReturnsCorrectPrice`
- `Activate_SingleDiscount_AppliesBestPrice`
- `Sweep_NoExpired_ReturnsEmpty`

This convention makes the test output readable. When a test fails, you immediately know what method broke and under what conditions.

### 6.2 Traits for Filtering

Every test method is decorated with three `[Trait]` attributes:

```csharp
[Trait("Category", "Unit")]
[Trait("Module", "PriceService")]
[Trait("Method", "CalculateEffectivePrice")]
```

This allows filtering in the test runner:

| Trait Key | Example Value | What It Filters |
|---|---|---|
| `Category` | `Unit` | All unit tests (vs. integration tests). |
| `Module` | `PriceService` or `LifecycleService` | Tests for a specific service. |
| `Method` | `ResolveTierForProduct` | Tests for a specific method. |
| `Scenario` | `ExcludeDiscount` | Tests for a specific scenario across methods. |

With these traits, a developer can run just the `ApplyBestDiscounts` tests, or just the `LifecycleService` tests, or just the concurrency‑related tests, without touching anything else.

---

## 7. How the Tests Map to the Production Architecture

The test suite directly mirrors the refactored service architecture:

```
┌─────────────────────────────────────────────┐
│ DiscountLifecycleServiceTests (17 tests)     │
│   Orchestrator behaviour verification        │
│   Mocks: CrudService, PriceService,          │
│          Hangfire, Cache, Notification       │
└─────────────────────────────────────────────┘
                    │
                    │ Verifies coordination of
                    ▼
┌─────────────────────────────────────────────┐
│ DiscountPriceServiceTests (25 tests)         │
│   Pure domain logic verification             │
│   Mocks: UnitOfWork, DbContext, Logger       │
└─────────────────────────────────────────────┘
```

The **`PriceService` tests** verify that the system *calculates the right answer*. They answer: “Given these products and these discounts, what price should appear?”

The **`LifecycleService` tests** verify that the system *does the right thing at the right time*. They answer: “When an admin updates a discount, does the system restore prices first? Then update the entity? Then recalculate? Then clear the cache?”

Together, these 42 tests form a safety net that catches regressions in the most critical parts of the discount engine.

---

## 8. Running the Tests

### Command Line

```bash
dotnet test Lili.Shop.Tests.csproj --filter "Category=Unit"
```

### Filtering by Module

```bash
# Price service only
dotnet test --filter "Module=PriceService"

# Lifecycle service only
dotnet test --filter "Module=LifecycleService"

# All deactivation tests
dotnet test --filter "Method=DeactivateDiscountByIdAsync"
```

### In Visual Studio

Open the **Test Explorer** (Test → Test Explorer). Use the search box to filter by trait:

- `Trait:"Module"` `Trait:"PriceService"`
- `Trait:"Scenario"` `Trait:"ExcludeDiscount"`

All 42 tests run in under 5 seconds because they never touch a database.

---

## 9. How to Add New Tests

When you add a new method to `DiscountPriceService` or `DiscountLifecycleService`, follow these conventions:

1. **Add a new `[Trait]` attribute** for the method name.
2. **Follow the naming pattern:** `{Method}_{Scenario}_{ExpectedBehavior}`.
3. **Use the existing helpers** (`CreateValidDto`, `MockDiscountDbSet`, etc.) to keep setup minimal.
4. **For the PriceService:** Mock only `_unitOfWork` and `_dbContextMock`. Test the calculated result directly.
5. **For the LifecycleService:** Set up the sibling service mocks to return expected data, then **verify** that they were called with the correct arguments and in the correct order.
6. **Add the new test to the coverage matrix** in this document.

---

## 10. Conclusion

The unit test suite for the LiliShop discount system provides thorough coverage of the two services that contain the most critical business logic. With 42 tests spanning price calculations, tier resolution, the best‑price engine, activation, deactivation, updates, sweeping, and deletion, the suite gives developers confidence that the discount engine behaves correctly under all defined scenarios.

Key strengths of the test suite:

- **Fast feedback** – All tests run in seconds, no database required.
- **Minimal setup per test** – Base classes and helper factories eliminate boilerplate.
- **Clear failure messages** – The naming convention tells you exactly what broke.
- **Filterable** – Traits allow running exactly the subset of tests you need.
- **Extensible** – Adding a new scenario means adding one short test method.

The CRUD and query services will be covered by integration tests in a separate suite, completing the testing pyramid for the entire discount system.
