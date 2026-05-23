using LiliShop.Application.Interfaces.Data;
using LiliShop.Application.Interfaces.Repositories;
using LiliShop.Domain.Entities.DiscountSystem;
using LiliShop.Infrastructure.Services.Discounts;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;

namespace Lili.Shop.Tests.Services.Discounts
{
    public abstract class DiscountPriceServiceTestBase
    {
        protected readonly Mock<ILogger<DiscountPriceService>> _loggerMock;
        protected readonly Mock<IUnitOfWork> _unitOfWorkMock;
        protected readonly Mock<IShopDbContext> _dbContextMock;

        protected readonly DiscountPriceService _sut;

        protected DiscountPriceServiceTestBase()
        {
            _loggerMock = new Mock<ILogger<DiscountPriceService>>();
            _unitOfWorkMock = new Mock<IUnitOfWork>();
            _dbContextMock = new Mock<IShopDbContext>();

            _unitOfWorkMock.Setup(u => u.Context).Returns(_dbContextMock.Object);
            _unitOfWorkMock.Setup(u => u.CompleteAsync()).ReturnsAsync(1);

            _sut = new DiscountPriceService(_unitOfWorkMock.Object, _loggerMock.Object);
        }

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
    }
}