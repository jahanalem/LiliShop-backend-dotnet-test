using Lili.Shop.API.Controllers;
using LiliShop.Application.Common.Pagination;
using LiliShop.Application.Common.Results;
using LiliShop.Application.Interfaces.Services;
using LiliShop.Application.Specifications.Params;
using LiliShop.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Moq;


namespace Lili.Shop.API.Tests
{
    public class ProductBrandControllerTests
    {
        private readonly Mock<IProductBrandService> _mockProductBrandService;
        private readonly ProductBrandController _controller;
        private BrandSpecParams _validBrandParams;
        private List<ProductBrand> _brands;
        private Pagination<ProductBrand> _paginatedResult;

        public ProductBrandControllerTests()
        {
            _mockProductBrandService = new Mock<IProductBrandService>();
            _controller = new ProductBrandController(_mockProductBrandService.Object);

            SetupSharedMockBehavior();
        }

        // Shared setup method for the mock and common data
        private void SetupSharedMockBehavior()
        {
            _validBrandParams = new BrandSpecParams { PageIndex = 1, PageSize = 10 };

            _brands = new List<ProductBrand>
            {
                new ProductBrand { Id = 1, Name = "Brand1", IsActive = true },
                new ProductBrand { Id = 2, Name = "Brand2", IsActive = true }
            };

            _paginatedResult = new Pagination<ProductBrand>(
                _validBrandParams.PageIndex,
                _validBrandParams.PageSize,
                _brands.Count,
                _brands
            );

            // Mock for invalid parameters
            _mockProductBrandService.Setup(service => service.GetPaginatedBrandsAsync(It.Is<BrandSpecParams>(p => p.PageIndex <= 0 || p.PageSize <= 0 && (p.IsActive == true || p.IsActive == false || p.IsActive == null))))
                .ReturnsAsync(new FailureOperationResult<Pagination<ProductBrand>>(ErrorCode.InvalidData, "Invalid pagination parameters"));

            // Mock for valid parameters
            _mockProductBrandService.Setup(service => service.GetPaginatedBrandsAsync(It.Is<BrandSpecParams>(p => p.PageIndex > 0 && p.PageSize > 0 && (p.IsActive == true || p.IsActive == false))))
                .ReturnsAsync(new SuccessOperationResult<Pagination<ProductBrand>>(_paginatedResult));
        }

        #region Test GetBrands

        [Fact]
        public async void GetBrands_ReturnsPaginatedResult_WhenBrandsExist()
        {
            // Arrange
            var brandParams = new BrandSpecParams { PageIndex = 1, PageSize = 10, IsActive = true };

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

        [Theory]
        [InlineData(1, 10)]
        public async void GetBrands_ReturnsOk_WhenParamsAreValid(int pageIndex, int pageSize)
        {
            // Arrange
            var validBrandParams = new BrandSpecParams { PageIndex = pageIndex, PageSize = pageSize, IsActive = true };

            // Act
            var result = await _controller.GetBrands(validBrandParams);

            // Assert
            var actionResult = Assert.IsType<ActionResult<Pagination<ProductBrand>>>(result);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            Assert.Equal(200, okResult.StatusCode);
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