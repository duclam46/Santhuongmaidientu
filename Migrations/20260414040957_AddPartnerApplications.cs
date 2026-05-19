using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOAN_BANHANG.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartnerApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ShopName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ShopDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProvinceId = table.Column<int>(type: "int", nullable: false),
                    ProvinceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DistrictId = table.Column<int>(type: "int", nullable: false),
                    DistrictName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WardCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WardName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StreetAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByAccountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerApplications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerApplications_AccountId",
                table: "PartnerApplications",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerApplications_CreatedAt",
                table: "PartnerApplications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerApplications_Email",
                table: "PartnerApplications",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerApplications_Status",
                table: "PartnerApplications",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerApplications");
        }
    }
}
