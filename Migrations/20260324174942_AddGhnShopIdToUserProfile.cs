using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOAN_BANHANG.Migrations
{
    /// <inheritdoc />
    public partial class AddGhnShopIdToUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GhnShopId",
                table: "UserProfiles",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GhnShopId",
                table: "UserProfiles");
        }
    }
}
