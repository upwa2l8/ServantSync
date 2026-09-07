using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class _fix_model_snapshot_reconcile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_ServiceSlots_ServiceSlotId",
                table: "Assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_MinistryInterests_Ministries_MinistryId",
                table: "MinistryInterests");

            migrationBuilder.DropForeignKey(
                name: "FK_Players_People_PrimaryContactPersonUserId",
                table: "Players");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceSlots_People_CoordinatorPersonUserId",
                table: "ServiceSlots");

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_People_CoachPersonUserId",
                table: "Teams");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingRequirements_Organizations_OrganizationId",
                table: "TrainingRequirements");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingRequirements_ServiceSlots_ServiceSlotId",
                table: "TrainingRequirements");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingSessions_TrainingContents_TrainingContentId",
                table: "TrainingSessions");

            migrationBuilder.AlterColumn<string>(
                name: "FilePathOrUrl",
                table: "TrainingContents",
                type: "TEXT",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 600);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "ServiceSlots",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "Ministries",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FeatureRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    SubmitterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SubmitterEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    SubmitterUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SubmitterIp = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TriageNotes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    LinkedSpec = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Honeypot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TriagedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TriagedByUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlotInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceSlotId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubscribedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotInterests_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SlotInterests_ServiceSlots_ServiceSlotId",
                        column: x => x.ServiceSlotId,
                        principalTable: "ServiceSlots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureRequests_CreatedUtc",
                table: "FeatureRequests",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureRequests_Status",
                table: "FeatureRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureRequests_SubmitterIp",
                table: "FeatureRequests",
                column: "SubmitterIp");

            migrationBuilder.CreateIndex(
                name: "IX_SlotInterests_PersonUserId",
                table: "SlotInterests",
                column: "PersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SlotInterests_PersonUserId_ServiceSlotId",
                table: "SlotInterests",
                columns: new[] { "PersonUserId", "ServiceSlotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlotInterests_ServiceSlotId",
                table: "SlotInterests",
                column: "ServiceSlotId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_ServiceSlots_ServiceSlotId",
                table: "Assignments",
                column: "ServiceSlotId",
                principalTable: "ServiceSlots",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MinistryInterests_Ministries_MinistryId",
                table: "MinistryInterests",
                column: "MinistryId",
                principalTable: "Ministries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_People_PrimaryContactPersonUserId",
                table: "Players",
                column: "PrimaryContactPersonUserId",
                principalTable: "People",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceSlots_People_CoordinatorPersonUserId",
                table: "ServiceSlots",
                column: "CoordinatorPersonUserId",
                principalTable: "People",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_People_CoachPersonUserId",
                table: "Teams",
                column: "CoachPersonUserId",
                principalTable: "People",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingRequirements_Organizations_OrganizationId",
                table: "TrainingRequirements",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingRequirements_ServiceSlots_ServiceSlotId",
                table: "TrainingRequirements",
                column: "ServiceSlotId",
                principalTable: "ServiceSlots",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingSessions_TrainingContents_TrainingContentId",
                table: "TrainingSessions",
                column: "TrainingContentId",
                principalTable: "TrainingContents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_ServiceSlots_ServiceSlotId",
                table: "Assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_MinistryInterests_Ministries_MinistryId",
                table: "MinistryInterests");

            migrationBuilder.DropForeignKey(
                name: "FK_Players_People_PrimaryContactPersonUserId",
                table: "Players");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceSlots_People_CoordinatorPersonUserId",
                table: "ServiceSlots");

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_People_CoachPersonUserId",
                table: "Teams");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingRequirements_Organizations_OrganizationId",
                table: "TrainingRequirements");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingRequirements_ServiceSlots_ServiceSlotId",
                table: "TrainingRequirements");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingSessions_TrainingContents_TrainingContentId",
                table: "TrainingSessions");

            migrationBuilder.DropTable(
                name: "FeatureRequests");

            migrationBuilder.DropTable(
                name: "SlotInterests");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "ServiceSlots");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "Ministries");

            migrationBuilder.AlterColumn<string>(
                name: "FilePathOrUrl",
                table: "TrainingContents",
                type: "TEXT",
                maxLength: 600,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_ServiceSlots_ServiceSlotId",
                table: "Assignments",
                column: "ServiceSlotId",
                principalTable: "ServiceSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MinistryInterests_Ministries_MinistryId",
                table: "MinistryInterests",
                column: "MinistryId",
                principalTable: "Ministries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Players_People_PrimaryContactPersonUserId",
                table: "Players",
                column: "PrimaryContactPersonUserId",
                principalTable: "People",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceSlots_People_CoordinatorPersonUserId",
                table: "ServiceSlots",
                column: "CoordinatorPersonUserId",
                principalTable: "People",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_People_CoachPersonUserId",
                table: "Teams",
                column: "CoachPersonUserId",
                principalTable: "People",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingRequirements_Organizations_OrganizationId",
                table: "TrainingRequirements",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingRequirements_ServiceSlots_ServiceSlotId",
                table: "TrainingRequirements",
                column: "ServiceSlotId",
                principalTable: "ServiceSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingSessions_TrainingContents_TrainingContentId",
                table: "TrainingSessions",
                column: "TrainingContentId",
                principalTable: "TrainingContents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
