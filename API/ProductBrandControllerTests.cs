using AutoMapper;
using Lili.Shop.API.Controllers;
using Lili.Shop.API.Errors;
using Lili.Shop.DataAccess.Specifications;
using Lili.Shop.Model.Entities;
using Lili.Shop.Service.Helpers;
using Lili.Shop.Service.Helpers.OperationResults;
using Lili.Shop.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;


namespace Lili.Shop.API.Tests
{
    public class ProductBrandControllerTests
    {
        private readonly Mock<IProductBrandService> _mockProductBrandService;
        private readonly ProductBrandController _controller;

        public ProductBrandControllerTests()
        {
            _mockProductBrandService = new Mock<IProductBrandService>();
            _controller = new ProductBrandController(_mockProductBrandService.Object);
        }

        #region Test GetBrands

        [Fact]
        public async void GetBrands_ReturnsPaginatedResult_WhenBrandsExist()
        {
            // Arrange
            var brandParams = new BrandSpecParams { PageIndex = 1, PageSize = 10 };
            var brands = new List<ProductBrand> {
                new ProductBrand { Id = 1, Name = "Brand1", IsActive = true },
                new ProductBrand { Id = 2, Name = "Brand2", IsActive = true }
            };
            var paginatedResult = new Pagination<ProductBrand>(brandParams.PageIndex, brandParams.PageSize, brands.Count, brands);
            _mockProductBrandService.Setup(service => service.GetPaginatedBrandsAsync(brandParams))
                .ReturnsAsync(new SuccessOperationResult<Pagination<ProductBrand>>(paginatedResult));

            // Act
            var result = await _controller.GetBrands(brandParams);

            // Assert
            var actionResult = Assert.IsType<ActionResult<Pagination<ProductBrand>>>(result);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            Assert.Equal(200, okResult.StatusCode);
            var returnedBrands = Assert.IsType<Pagination<ProductBrand>>(okResult.Value);
            Assert.Equal(2, returnedBrands.Count);
            Assert.Equal(brandParams.PageIndex, returnedBrands.PageIndex);
            Assert.Equal(brandParams.PageSize, returnedBrands.PageSize);
        }

        [Theory]
        [InlineData(-1, 10)]
        [InlineData(1, -10)]
        [InlineData(0, 10)]
        [InlineData(1, 0)]
        public async void GetBrands_ReturnsBadRequest_WhenParamsAreInvalid(int pageIndex, int pageSize)
        {
            // Arrange
            var invalidBrandParams = new BrandSpecParams { PageIndex = pageIndex, PageSize = pageSize };

            // Act
            var result = await _controller.GetBrands(invalidBrandParams);

            // Assert
            var actionResult = Assert.IsType<ActionResult<Pagination<ProductBrand>>>(result);
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        #endregion

        #region Test GetProductBrandById 

        [Fact]
        public async void GetProductBrandById_ReturnsNotFound_WhenBrandDoesNotExist()
        {
            // Arrange
            int brandId = 1000;
            _mockProductBrandService.Setup(service => service.GetBrandByIdAsync(brandId))
                .ReturnsAsync(new FailureOperationResult<ProductBrand>(ErrorCode.ResourceNotFound, "Brand not found"));

            // Act
            var result = await _controller.GetProductBrandById(brandId);

            // Assert
            var actionResult = Assert.IsType<ActionResult<ProductBrand>>(result);
            Assert.IsType<ObjectResult>(actionResult.Result);
            var notFoundResult = actionResult.Result as ObjectResult;
            Assert.Equal(404, notFoundResult?.StatusCode);
        }

        [Fact]
        public async Task GetProductBrandById_ReturnsProductBrand_WhenBrandExists()
        {
            // Arrange
            int brandId = 1;
            var expectedBrand = new ProductBrand
            {
                Id = brandId,
                Name = "Test Brand",
                IsActive = true
            };

            _mockProductBrandService.Setup(service => service.GetBrandByIdAsync(brandId))
                .ReturnsAsync(new SuccessOperationResult<ProductBrand>(expectedBrand));

            // Act
            var result = await _controller.GetProductBrandById(brandId);

            // Assert
            var actionResult = Assert.IsType<ActionResult<ProductBrand>>(result);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            Assert.Equal(200, okResult.StatusCode);

            var returnedBrand = Assert.IsType<ProductBrand>(okResult.Value);
            Assert.Equal(expectedBrand.Id, returnedBrand.Id);
            Assert.Equal(expectedBrand.Name, returnedBrand.Name);
            Assert.Equal(expectedBrand.IsActive, returnedBrand.IsActive);
        }

        [Fact]
        public async void GetProductBrandById_ReturnsBadRequest_WhenIdIsInvalid()
        {
            // Arange
            int inValidId = -1;

            // Act
            var result = await _controller.GetProductBrandById(inValidId);

            // Assert
            var actionResult = Assert.IsType<ActionResult<ProductBrand>>(result);
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        [Fact]
        public async void GetProductBrandById_ReturnsBadRequest_WhenIdIsZero()
        {
            // Arrange
            int invalidId = 0;

            // Act
            var result = await _controller.GetProductBrandById(invalidId);

            // Assert
            var actionResult = Assert.IsType<ActionResult<ProductBrand>>(result);
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        [Fact]
        public async void GetProductBrandById_ReturnsFailureResult_WhenServiceFails()
        {
            // Arrange
            int brandId = 1;
            _mockProductBrandService.Setup(service => service.GetBrandByIdAsync(brandId))
                .ReturnsAsync(new FailureOperationResult<ProductBrand>(ErrorCode.GeneralException, "Service failure"));

            // Act
            var result = await _controller.GetProductBrandById(brandId);

            // Assert
            var actionResult = Assert.IsType<ActionResult<ProductBrand>>(result);
            var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
            Assert.Equal(500, objectResult.StatusCode);
        }

        #endregion
    }
}