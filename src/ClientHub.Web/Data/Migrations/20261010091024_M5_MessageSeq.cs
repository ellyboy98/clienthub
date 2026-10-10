using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClientHub.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class M5_MessageSeq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "seq",
                table: "messages",
                type: "bigint",
                nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

            migrationBuilder.CreateIndex(
                name: "ix_messages_client_id_seq",
                table: "messages",
                columns: new[] { "client_id", "seq" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_messages_client_id_seq",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "seq",
                table: "messages");
        }
    }
}
