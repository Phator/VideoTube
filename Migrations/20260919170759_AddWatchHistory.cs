using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoTube.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WatchHistories_AspNetUsers_UserId",
                table: "WatchHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_WatchHistories_Videos_VideoId",
                table: "WatchHistories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WatchHistories",
                table: "WatchHistories");

            migrationBuilder.RenameTable(
                name: "WatchHistories",
                newName: "WatchHistory");

            migrationBuilder.RenameIndex(
                name: "IX_WatchHistories_VideoId",
                table: "WatchHistory",
                newName: "IX_WatchHistory_VideoId");

            migrationBuilder.RenameIndex(
                name: "IX_WatchHistories_UserId",
                table: "WatchHistory",
                newName: "IX_WatchHistory_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WatchHistory",
                table: "WatchHistory",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WatchHistory_AspNetUsers_UserId",
                table: "WatchHistory",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WatchHistory_Videos_VideoId",
                table: "WatchHistory",
                column: "VideoId",
                principalTable: "Videos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WatchHistory_AspNetUsers_UserId",
                table: "WatchHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_WatchHistory_Videos_VideoId",
                table: "WatchHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WatchHistory",
                table: "WatchHistory");

            migrationBuilder.RenameTable(
                name: "WatchHistory",
                newName: "WatchHistories");

            migrationBuilder.RenameIndex(
                name: "IX_WatchHistory_VideoId",
                table: "WatchHistories",
                newName: "IX_WatchHistories_VideoId");

            migrationBuilder.RenameIndex(
                name: "IX_WatchHistory_UserId",
                table: "WatchHistories",
                newName: "IX_WatchHistories_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WatchHistories",
                table: "WatchHistories",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WatchHistories_AspNetUsers_UserId",
                table: "WatchHistories",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WatchHistories_Videos_VideoId",
                table: "WatchHistories",
                column: "VideoId",
                principalTable: "Videos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
