using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolAdmission.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "classes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    total_seats = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classes", x => x.id);
                    table.CheckConstraint("ck_classes_total_seats_positive", "total_seats > 0");
                });

            migrationBuilder.CreateTable(
                name: "holds",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holds", x => x.id);
                    table.CheckConstraint("ck_holds_status", "status IN ('HELD','CONFIRMED','CANCELLED')");
                    table.CheckConstraint("ck_holds_student_id_format", "student_id ~ '^[0-9]{17}$'");
                    table.ForeignKey(
                        name: "fk_holds_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_classes_normalized_name",
                table: "classes",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holds_class_id",
                table: "holds",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_holds_class_id_status_expires_at",
                table: "holds",
                columns: new[] { "class_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_holds_class_id_student_id",
                table: "holds",
                columns: new[] { "class_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ix_holds_student_id_created_at",
                table: "holds",
                columns: new[] { "student_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_holds_class_id_student_id_confirmed",
                table: "holds",
                columns: new[] { "class_id", "student_id" },
                unique: true,
                filter: "status = 'CONFIRMED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holds");

            migrationBuilder.DropTable(
                name: "classes");
        }
    }
}
