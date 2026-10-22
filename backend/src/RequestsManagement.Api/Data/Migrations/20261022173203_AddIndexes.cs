using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RequestsManagement.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestStatusHistory_RequestId",
                table: "RequestStatusHistory");

            migrationBuilder.CreateIndex(
                name: "IX_RequestStatusHistory_RequestId_ChangedAt",
                table: "RequestStatusHistory",
                columns: new[] { "RequestId", "ChangedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_AssignedTo_CreatedAt",
                table: "Requests",
                columns: new[] { "AssignedTo", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CreatedAt",
                table: "Requests",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_OrganizationName",
                table: "Requests",
                column: "OrganizationName");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Priority_CreatedAt",
                table: "Requests",
                columns: new[] { "Priority", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status_CreatedAt",
                table: "Requests",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestStatusHistory_RequestId_ChangedAt",
                table: "RequestStatusHistory");

            migrationBuilder.DropIndex(
                name: "IX_Requests_AssignedTo_CreatedAt",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_CreatedAt",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_OrganizationName",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_Priority_CreatedAt",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_Status_CreatedAt",
                table: "Requests");

            migrationBuilder.CreateIndex(
                name: "IX_RequestStatusHistory_RequestId",
                table: "RequestStatusHistory",
                column: "RequestId");
        }
    }
}
