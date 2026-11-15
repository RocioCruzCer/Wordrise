using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wordrise.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingAndEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_rounds_daily_link",
                table: "rounds");

            migrationBuilder.AddColumn<string>(
                name: "emoji",
                table: "words",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "daily_goal",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "show_in_leagues",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "sound_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "streak_freezes",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "emoji_hint_used",
                table: "rounds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "xp",
                table: "rounds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    owner_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groups", x => x.id);
                    table.CheckConstraint("ck_groups_code", "code ~ '^[A-Z2-9]{6}$'");
                    table.CheckConstraint("ck_groups_name", "char_length(trim(name)) BETWEEN 3 AND 40");
                    table.ForeignKey(
                        name: "fk_groups_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sprint_sessions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    played_on = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    xp = table.Column<int>(type: "integer", nullable: false),
                    challenge_number = table.Column<int>(type: "integer", nullable: false),
                    current_word_id = table.Column<int>(type: "integer", nullable: true),
                    current_letters = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    seen_word_ids = table.Column<List<int>>(type: "integer[]", nullable: false),
                    solved_word_ids = table.Column<List<int>>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sprint_sessions", x => x.id);
                    table.CheckConstraint("ck_sprint_sessions_counts", "score >= 0 AND skipped >= 0 AND xp >= 0");
                    table.CheckConstraint("ck_sprint_sessions_level", "level IN ('A2','B1','B2')");
                    table.CheckConstraint("ck_sprint_sessions_window", "ends_at > started_at");
                    table.ForeignKey(
                        name: "fk_sprint_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sprint_sessions_words_current_word_id",
                        column: x => x.current_word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "streak_freeze_days",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streak_freeze_days", x => new { x.user_id, x.date });
                    table.ForeignKey(
                        name: "fk_streak_freeze_days_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_achievements",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unlocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    seen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_achievements", x => new { x.user_id, x.code });
                    table.ForeignKey(
                        name: "fk_user_achievements_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_members",
                columns: table => new
                {
                    group_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_members", x => new { x.group_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_group_members_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_daily_goal",
                table: "users",
                sql: "daily_goal BETWEEN 1 AND 20");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_streak_freezes",
                table: "users",
                sql: "streak_freezes BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rounds_daily_link",
                table: "rounds",
                sql: "(mode = 'Daily' AND daily_word_id IS NOT NULL) OR (mode <> 'Daily' AND daily_word_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rounds_mode",
                table: "rounds",
                sql: "mode IN ('Daily','Review','Practice','Listening')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rounds_xp",
                table: "rounds",
                sql: "xp >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_group_members_user_id",
                table: "group_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_code",
                table: "groups",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_groups_owner_user_id",
                table: "groups",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sprint_sessions_current_word_id",
                table: "sprint_sessions",
                column: "current_word_id");

            migrationBuilder.CreateIndex(
                name: "ix_sprint_sessions_user_id_played_on",
                table: "sprint_sessions",
                columns: new[] { "user_id", "played_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_members");

            migrationBuilder.DropTable(
                name: "sprint_sessions");

            migrationBuilder.DropTable(
                name: "streak_freeze_days");

            migrationBuilder.DropTable(
                name: "user_achievements");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_daily_goal",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_streak_freezes",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rounds_daily_link",
                table: "rounds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rounds_mode",
                table: "rounds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rounds_xp",
                table: "rounds");

            migrationBuilder.DropColumn(
                name: "emoji",
                table: "words");

            migrationBuilder.DropColumn(
                name: "daily_goal",
                table: "users");

            migrationBuilder.DropColumn(
                name: "show_in_leagues",
                table: "users");

            migrationBuilder.DropColumn(
                name: "sound_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "streak_freezes",
                table: "users");

            migrationBuilder.DropColumn(
                name: "emoji_hint_used",
                table: "rounds");

            migrationBuilder.DropColumn(
                name: "xp",
                table: "rounds");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rounds_daily_link",
                table: "rounds",
                sql: "(mode = 'Daily' AND daily_word_id IS NOT NULL) OR (mode = 'Review' AND daily_word_id IS NULL)");
        }
    }
}
