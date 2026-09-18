using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoTube.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThumbnailFileName",
                table: "Videos",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailFileName",
                table: "Videos");
        }
    }
}
