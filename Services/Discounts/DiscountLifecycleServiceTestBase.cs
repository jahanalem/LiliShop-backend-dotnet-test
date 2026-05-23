using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hangfire;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using LiliShop.Application.Interfaces.Services;
using LiliShop.Application.Interfaces.Services.Discounts;
using LiliShop.Application.DTOs.Discounts;
using LiliShop.Application.Common.Results;
using LiliShop.Application.Interfaces.Repositories;
using LiliShop.Application.Caching;
using LiliShop.Domain.Entities.DiscountSystem;
using LiliShop.Infrastructure.Services.Discounts;
using LiliShop.Infrastructure.Repositories; 
using LiliShop.Application.Interfaces.Data;
using MockQueryable.Moq;


namespace Lili.Shop.Tests.Services.Discounts
{
    public abstract class DiscountLifecycleServiceTestBase
    {
        protected readonly Mock<IDiscountCrudService> _crudServiceMock;
        protected readonly Mock<IDiscountQueryService> _queryServiceMock;
        protected readonly Mock<IDiscountPriceService> _priceServiceMock;
        protected readonly Mock<IUnitOfWork> _unitOfWorkMock;
        protected readonly Mock<IShopDbContext> _dbContextMock;
        protected readonly Mock<ICacheManagerService> _cacheManagerMock;
        protected readonly Mock<INotificationService> _notificationServiceMock;
        protected readonly Mock<ILogger<DiscountLifecycleService>> _loggerMock;
        protected readonly Mock<IBackgroundJobClient> _backgroundJobClientMock;

        protected readonly DiscountLifecycleService _sut;

        protected DiscountLifecycleServiceTestBase()
        {
            _crudServiceMock = new Mock<IDiscountCrudService>();
            _queryServiceMock = new Mock<IDiscountQueryService>();
            _priceServiceMock = new Mock<IDiscountPriceService>();
            _unitOfWorkMock = new Mock<IUnitOfWork>();
            _dbContextMock = new Mock<IShopDbContext>();
            _cacheManagerMock = new Mock<ICacheManagerService>();
            _notificationServiceMock = new Mock<INotificationService>();
            _loggerMock = new Mock<ILogger<DiscountLifecycleService>>();
            _backgroundJobClientMock = new Mock<IBackgroundJobClient>();
            _backgroundJobClientMock
                .Setup(c => c.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()))
                .Returns("test-job-id");

            _unitOfWorkMock.Setup(u => u.Context).Returns(_dbContextMock.Object);
            _unitOfWorkMock.Setup(u => u.CompleteAsync()).ReturnsAsync(1);

            _sut = new DiscountLifecycleService(
                _crudServiceMock.Object,
                _queryServiceMock.Object,
                _priceServiceMock.Object,
                _unitOfWorkMock.Object,
                _cacheManagerMock.Object,
                new Lazy<INotificationService>(() => _notificationServiceMock.Object),
                _loggerMock.Object,
                _backgroundJobClientMock.Object
            );
        }

        protected Mock<IGenericRepository<Discount>> SetupDiscountRepository(Discount discount)
        {
            return SetupDiscountRepositoryWithList(new List<Discount> { discount });
        }

        protected Mock<IGenericRepository<Discount>> SetupDiscountRepositoryWithList(List<Discount> discounts)
        {
            var repoMock = new Mock<IGenericRepository<Discount>>();
            var mockDbSet = discounts.BuildMockDbSet<Discount>();
            repoMock.Setup(r => r.GetByCriteria(It.IsAny<System.Linq.Expressions.Expression<Func<Discount, bool>>>(), It.IsAny<bool>()))
                    .Returns(mockDbSet.Object);
            _unitOfWorkMock.Setup(u => u.Repository<Discount>()).Returns(repoMock.Object);
            return repoMock;
        }

        // Reusable mock helpers for DbSet (same as in price service tests)
        protected void MockDiscountDbSet(List<Discount> discounts)
        {
            var mockDbSet = discounts.BuildMockDbSet<Discount>();
            _dbContextMock.Setup(c => c.Set<Discount>()).Returns(mockDbSet.Object);
        }

        protected void MockDbSet<TEntity>(List<TEntity> data) where TEntity : class
        {
            var mockSet = data.BuildMockDbSet<TEntity>();
            _dbContextMock.Setup(c => c.Set<TEntity>()).Returns(mockSet.Object);
        }

        // Helper to create a minimal CreateDiscountDto with valid dates
        protected CreateDiscountDto CreateValidDto(
            string name = "Test Discount",
            DateTimeOffset? startDate = null,
            DateTimeOffset? endDate = null,
            bool isActive = true,
            bool hasGroup = false)
        {
            var dto = new CreateDiscountDto
            {
                Name = name,
                StartDate = startDate ?? DateTimeOffset.UtcNow.AddDays(-1),
                EndDate = endDate ?? DateTimeOffset.UtcNow.AddDays(30),
                IsActive = isActive,
                Tiers = new List<DiscountTierDto>
                {
                    new DiscountTierDto { Amount = 10, IsPercentage = false }
                }
            };

            if (hasGroup)
            {
                dto.DiscountGroup = new DiscountGroupDto
                {
                    Name = "Test Group",
                    ConditionGroups = new List<ConditionGroupDto>
                    {
                        new ConditionGroupDto
                        {
                            TierIndex = 0,
                            Conditions = new List<DiscountGroupConditionDto>
                            {
                                new DiscountGroupConditionDto
                                {
                                    TargetEntity = DiscountTargetType.All
                                }
                            }
                        }
                    }
                };
            }

            return dto;
        }

        // Helper to create a Discount entity from a CreateDiscountDto
        protected Discount CreateDiscountFromDto(CreateDiscountDto dto, int id = 1)
        {
            return new Discount
            {
                Id = id,
                Name = dto.Name,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                IsActive = dto.IsActive,
                Tiers = new List<DiscountTier>
                {
                    new DiscountTier { Id = 100, Amount = dto.Tiers[0].Amount, IsPercentage = dto.Tiers[0].IsPercentage }
                },
                DiscountGroup = dto.DiscountGroup != null ? new DiscountGroup
                {
                    Name = dto.DiscountGroup.Name,
                    ConditionGroups = new List<ConditionGroup>
                    {
                        new ConditionGroup
                        {
                            DiscountTierId = 100,
                            DiscountGroupConditions = new List<DiscountGroupCondition>
                            {
                                new DiscountGroupCondition
                                {
                                    TargetEntity = DiscountTargetType.All,
                                    ShouldNotify = true
                                }
                            }
                        }
                    }
                } : null
            };
        }
    }
}