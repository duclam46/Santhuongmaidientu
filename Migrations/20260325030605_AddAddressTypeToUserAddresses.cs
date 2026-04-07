using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOAN_BANHANG.Migrations
{
    /// <inheritdoc />
    public partial class AddAddressTypeToUserAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAddresses_AccountId_IsDefault",
                table: "UserAddresses");

            migrationBuilder.AddColumn<int>(
                name: "AddressType",
                table: "UserAddresses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_AccountId_AddressType_IsDefault",
                table: "UserAddresses",
                columns: new[] { "AccountId", "AddressType", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAddresses_AccountId_AddressType_IsDefault",
                table: "UserAddresses");

            migrationBuilder.DropColumn(
                name: "AddressType",
                table: "UserAddresses");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_AccountId_IsDefault",
                table: "UserAddresses",
                columns: new[] { "AccountId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1");
        }
    }
}
