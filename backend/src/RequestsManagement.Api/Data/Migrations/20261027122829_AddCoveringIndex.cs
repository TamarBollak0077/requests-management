using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RequestsManagement.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status_CreatedAt_Covering",
                table: "Requests",
                columns: new[] { "Status", "CreatedAt", "Id" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "Title", "OrganizationName", "Priority", "AssignedTo", "UpdatedAt", "RowVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Requests_Status_CreatedAt_Covering",
                table: "Requests");
        }
    }
}
