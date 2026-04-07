using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOAN_BANHANG.Migrations
{
    /// <inheritdoc />
    public partial class AddGhnShippingFieldsToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShippingDistrictId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingProvinceId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingStreetAddress",
                table: "Orders",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingWardCode",
                table: "Orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingDistrictId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingProvinceId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingStreetAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingWardCode",
                table: "Orders");
        }
    }
}
