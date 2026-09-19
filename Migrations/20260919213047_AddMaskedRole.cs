using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoTube.Migrations
{
    public partial class AddMaskedRole : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
                SELECT NEWID(), 'Masked', 'MASKED', NEWID()
                WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'Masked');
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM AspNetRoles WHERE Name = 'Masked';
            ");
        }
    }
}
