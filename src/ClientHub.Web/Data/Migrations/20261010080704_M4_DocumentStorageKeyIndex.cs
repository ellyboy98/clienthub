using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHub.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class M4_DocumentStorageKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_documents_storage_key",
                table: "documents",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_storage_key",
                table: "documents");
        }
    }
}
