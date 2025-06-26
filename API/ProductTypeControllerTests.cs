using Lili.Shop.API.Controllers;
using LiliShop.Application.Common.Pagination;
using LiliShop.Application.Common.Results;
using LiliShop.Application.Interfaces.Services;
using LiliShop.Application.Specifications.Params;
using LiliShop.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Lili.Shop.Tests.API
{
    public class ProductTypeControllerTests
    {
        private readonly Mock<IProductTypeService> _mockProductTypeService;
        private readonly ProductTypeController _controller;

        private ProductTypeSpecParams _validTypeParams;
        private List<ProductType> _types;
        private Pagination<ProductType> _paginatedResult;

        public ProductTypeControllerTests()
        {
            _mockProductTypeService = new Mock<IProductTypeService>();
            _controller = new ProductTypeController(_mockProductTypeService.Object);

            SetupSharedMockBehavior();
        }

        // Shared setup method for the mock and common data
        private void SetupSharedMockBehavior()
        {
            _validTypeParams = new ProductTypeSpecParams { PageIndex = 1, PageSize = 10 };

            _types = new List<ProductType>
            {
                new ProductType { Id = 1, Name = "Type1", IsActive = true },
                new ProductType { Id = 2, Name = "Type2", IsActive = true }
            };

            _paginatedResult = new Pagination<ProductType>(
                _validTypeParams.PageIndex,
                _validTypeParams.PageSize,
                _types.Count,
                _types
            );

            // Mock for invalid parameters
            _mockProductTypeService.Setup(service => service.GetPaginatedProductTypesAsync(It.Is<ProductTypeSpecParams>(p => p.PageIndex <= 0 || p.PageSize <= 0)))
                .ReturnsAsync(new FailureOperationResult<Pagination<ProductType>>(ErrorCode.InvalidData, "Invalid pagination parameters"));

            // Mock for valid parameters
            _mockProductTypeService.Setup(service => service.GetPaginatedProductTypesAsync(It.Is<ProductTypeSpecParams>(p => p.PageIndex > 0 && p.PageSize > 0)))
                .ReturnsAsync(new SuccessOperationResult<Pagination<ProductType>>(_paginatedResult));
        }

        #region Test GetProductTypes

        [Fact]
        public async void GetTypes_ReturnsPaginatedResult_WhenTypesExist()
        {
            // Arrange
            var productTypeSpecParams = new ProductTypeSpecParams { PageIndex = 1, PageSize = 10 };


            // Act
            var result = await _controller.GetTypes(productTypeSpecParams);

            // Assert
            var actionResult = Assert.IsType<ActionResult<Pagination<ProductType>>>(result);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            Assert.Equal(200, okResult.StatusCode);

            var returnedProductTypes = Assert.IsType<Pagination<ProductType>>(okResult.Value);
            Assert.Equal(2, returnedProductTypes.Count);
            Assert.Equal(returnedProductTypes.PageSize, returnedProductTypes.PageSize);
            Assert.Equal(returnedProductTypes.PageIndex, returnedProductTypes.PageIndex);
        }

        [Theory]
        [InlineData(-1, 10)]
        [InlineData(1, -10)]
        [InlineData(0, 10)]
        [InlineData(1, 0)]
        public async void GetTypes__ReturnsBadRequest_WhenParamsAreInvalid(int pageIndex, int pageSize)
        {
            // Arrange
            var invalidParams = new ProductTypeSpecParams { PageIndex = pageIndex, PageSize = pageSize, IsActive = true };

            // Resutl
            var result = await _controller.GetTypes(invalidParams);

            // Assert
            var actionResult = Assert.IsType<ActionResult<Pagination<ProductType>>>(result);
            var badRequestObjectResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
            Assert.Equal(400, badRequestObjectResult.StatusCode);
        }

        [Theory]
        [InlineData(1, 10)]
        public async void GetTypes__ReturnsOk_WhenParamsAreValid(int pageIndex, int pageSize)
        {
            // Arrange
            var validParams = new ProductTypeSpecParams { PageIndex = pageIndex, PageSize = pageSize };

            // Act
            var result = await _controller.GetTypes(validParams);

            // Assert
            var actionResult = Assert.IsType<ActionResult<Pagination<ProductType>>>(result);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            Assert.Equal(200, okResult.StatusCode);
        }

        #endregion

        #region GetProductTypeById

        [Fact]
        public async void GetProductTypeById__ReturnsNotFound_WhenProductTypeDoesNotExist()
        {
            // Arrange
            int productTypeId = 1000;
            _mockProductTypeService.Setup(service => service.GetTypeByIdAsync(productTypeId))
                .ReturnsAsync(new FailureOperationResult<ProductType>(ErrorCode.ResourceNotFound, "Product Type not found"));

            // Act
            var result = await _controller.GetProductTypeById(productTypeId);

            // Assert
            var actionResult = Assert.IsType<ActionResult<ProductType>>(result);
            var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
            Assert.Equal(404, objectResult.StatusCode);
        }

        [Fact]
        public async void GetProductTypeById__ReturnsProductType_WhenTypeExists()
        {
            // Arrange
            var productTypeId = 1;
            var expectedProductType = new ProductType
            {
                Id = 1,
                Name = "Type1",
                IsActive = true,
            };
            _mockProductTypeService.Setup(service => service.GetTypeByIdAsync(productTypeId))
                .ReturnsAsync(new SuccessOperationResult<ProductType>(expectedProductType));

            // Act
            var result = await _controller.GetProductTypeById(productTypeId);

            // Assert
            var actionResult = Assert.IsType<ActionResult<ProductType>>(result);
            var objectResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            Assert.Equal(200, objectResult.StatusCode);
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(null)]
        public async void GetProductTypeById__ReturnsBadRequest_WhenParamsAreInvalid(int productTypeId)
        {
            // Arrange
            _mockProductTypeService.Setup(service => service.GetTypeByIdAsync(productTypeId))
                .ReturnsAsync(new FailureOperationResult<ProductType>(ErrorCode.InvalidData));

            // Act
            var result = await _controller.GetProductTypeById(productTypeId);

            // Assert
            var actionResult = Assert.IsType<ActionResult<ProductType>>(result);
            var badRequestObjectResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
            Assert.Equal(400, badRequestObjectResult.StatusCode);
        }

        #endregion
    }
}
