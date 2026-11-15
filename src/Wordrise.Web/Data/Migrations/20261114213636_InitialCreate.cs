using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wordrise.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    level = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    pronunciation_audio_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_email_lower", "email = lower(email)");
                    table.CheckConstraint("ck_users_level", "level IN ('A2','B1','B2')");
                });

            migrationBuilder.CreateTable(
                name: "words",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    level = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    part_of_speech = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    definition_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    example_sentence = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    translation_es = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_daily_answer = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_words", x => x.id);
                    table.CheckConstraint("ck_words_level", "level IN ('A2','B1','B2')");
                    table.CheckConstraint("ck_words_text_format", "text ~ '^[a-z]{4,10}$'");
                });

            migrationBuilder.CreateTable(
                name: "daily_words",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    level = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    word_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_words", x => x.id);
                    table.CheckConstraint("ck_daily_words_level", "level IN ('A2','B1','B2')");
                    table.ForeignKey(
                        name: "fk_daily_words_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pending_reviews",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    next_review_on = table.Column<DateOnly>(type: "date", nullable: false),
                    last_missed_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_reviews", x => x.id);
                    table.CheckConstraint("ck_pending_reviews_stage", "stage >= 0");
                    table.ForeignKey(
                        name: "fk_pending_reviews_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pending_reviews_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rounds",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    daily_word_id = table.Column<int>(type: "integer", nullable: true),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    played_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    attempts_used = table.Column<int>(type: "integer", nullable: false),
                    hints_unlocked = table.Column<int>(type: "integer", nullable: false),
                    manual_hints_used = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rounds", x => x.id);
                    table.CheckConstraint("ck_rounds_attempts", "attempts_used BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_rounds_daily_link", "(mode = 'Daily' AND daily_word_id IS NOT NULL) OR (mode = 'Review' AND daily_word_id IS NULL)");
                    table.CheckConstraint("ck_rounds_hints", "hints_unlocked BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_rounds_manual_hints", "manual_hints_used BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_rounds_score", "score >= 0");
                    table.ForeignKey(
                        name: "fk_rounds_daily_words_daily_word_id",
                        column: x => x.daily_word_id,
                        principalTable: "daily_words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rounds_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rounds_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attempts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    round_id = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    guess_text = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    feedback = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempts", x => x.id);
                    table.CheckConstraint("ck_attempts_number", "number BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "fk_attempts_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attempts_round_id_number",
                table: "attempts",
                columns: new[] { "round_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_words_date_level",
                table: "daily_words",
                columns: new[] { "date", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_words_word_id",
                table: "daily_words",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_reviews_user_id_next_review_on",
                table: "pending_reviews",
                columns: new[] { "user_id", "next_review_on" });

            migrationBuilder.CreateIndex(
                name: "ix_pending_reviews_user_id_word_id",
                table: "pending_reviews",
                columns: new[] { "user_id", "word_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_reviews_word_id",
                table: "pending_reviews",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_rounds_daily_word_id",
                table: "rounds",
                column: "daily_word_id");

            migrationBuilder.CreateIndex(
                name: "ix_rounds_user_id_daily_word_id",
                table: "rounds",
                columns: new[] { "user_id", "daily_word_id" },
                unique: true,
                filter: "daily_word_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rounds_user_id_played_on",
                table: "rounds",
                columns: new[] { "user_id", "played_on" });

            migrationBuilder.CreateIndex(
                name: "ix_rounds_word_id",
                table: "rounds",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_words_level_is_daily_answer",
                table: "words",
                columns: new[] { "level", "is_daily_answer" });

            migrationBuilder.CreateIndex(
                name: "ix_words_text",
                table: "words",
                column: "text",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempts");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "pending_reviews");

            migrationBuilder.DropTable(
                name: "rounds");

            migrationBuilder.DropTable(
                name: "daily_words");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "words");
        }
    }
}
