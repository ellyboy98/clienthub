using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHub.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class M5_MessageAttachmentKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_messages_attachment_key",
                table: "messages",
                column: "attachment_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_messages_attachment_key",
                table: "messages");
        }
    }
}
