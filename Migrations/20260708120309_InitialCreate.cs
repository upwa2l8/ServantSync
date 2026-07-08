using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RegistrationToken = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemAdminGrantAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAdminGrantAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsStub = table.Column<bool>(type: "bit", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BackgroundCheckDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SkillsNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_People_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Arenas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SurfaceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Arenas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Arenas_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Format = table.Column<int>(type: "int", nullable: false),
                    FilePathOrUrl = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    EstimatedDuration = table.Column<TimeSpan>(type: "time", nullable: true),
                    TotalPageCount = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    VersionDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VersionLabel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingContents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Ministries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CoordinatorPersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CoordinatorEmail = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CoordinatorPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ParentMinistryId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ministries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ministries_Ministries_ParentMinistryId",
                        column: x => x.ParentMinistryId,
                        principalTable: "Ministries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ministries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Ministries_People_CoordinatorPersonUserId",
                        column: x => x.CoordinatorPersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    JoinedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonClaimTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClaimedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonClaimTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonClaimTokens_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TrainingContentId = table.Column<int>(type: "int", nullable: false),
                    TrainingContentVersion = table.Column<int>(type: "int", nullable: false),
                    ViewedPagesCsv = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    HighestWatchedSec = table.Column<int>(type: "int", nullable: false),
                    ActualDurationSec = table.Column<int>(type: "int", nullable: false),
                    FirstOpenedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingActivities_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingActivities_TrainingContents_TrainingContentId",
                        column: x => x.TrainingContentId,
                        principalTable: "TrainingContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCompletions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TrainingContentId = table.Column<int>(type: "int", nullable: false),
                    TrainingContentVersion = table.Column<int>(type: "int", nullable: false),
                    CompletionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompletionSource = table.Column<int>(type: "int", nullable: false),
                    MarkedCompleteByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ManualCompletionNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCompletions_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCompletions_TrainingContents_TrainingContentId",
                        column: x => x.TrainingContentId,
                        principalTable: "TrainingContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    TrainingContentId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxAttendees = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_TrainingContents_TrainingContentId",
                        column: x => x.TrainingContentId,
                        principalTable: "TrainingContents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MinistryInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MinistryId = table.Column<int>(type: "int", nullable: false),
                    JoinedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinistryInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MinistryInterests_Ministries_MinistryId",
                        column: x => x.MinistryId,
                        principalTable: "Ministries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MinistryInterests_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinistryId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DefaultDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CoordinatorPersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CoordinatorEmail = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CoordinatorPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceSlots_Ministries_MinistryId",
                        column: x => x.MinistryId,
                        principalTable: "Ministries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceSlots_People_CoordinatorPersonUserId",
                        column: x => x.CoordinatorPersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinistryId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AgeBracket = table.Column<int>(type: "int", nullable: false),
                    Gender = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CoachPersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Ministries_MinistryId",
                        column: x => x.MinistryId,
                        principalTable: "Ministries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Teams_People_CoachPersonUserId",
                        column: x => x.CoachPersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "TrainingSessionAttendees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrainingSessionId = table.Column<int>(type: "int", nullable: false),
                    PersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SignedUpUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attended = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingSessionAttendees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingSessionAttendees_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingSessionAttendees_TrainingSessions_TrainingSessionId",
                        column: x => x.TrainingSessionId,
                        principalTable: "TrainingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServiceSlotId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assignments_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assignments_ServiceSlots_ServiceSlotId",
                        column: x => x.ServiceSlotId,
                        principalTable: "ServiceSlots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SlotDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServiceSlotId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UploadedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotDocuments_People_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlotDocuments_ServiceSlots_ServiceSlotId",
                        column: x => x.ServiceSlotId,
                        principalTable: "ServiceSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SlotOccurrences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServiceSlotId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CapacityOverride = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotOccurrences_ServiceSlots_ServiceSlotId",
                        column: x => x.ServiceSlotId,
                        principalTable: "ServiceSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingRequirements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: true),
                    ServiceSlotId = table.Column<int>(type: "int", nullable: true),
                    TrainingContentId = table.Column<int>(type: "int", nullable: false),
                    Cadence = table.Column<int>(type: "int", nullable: false),
                    CadenceMonths = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingRequirements", x => x.Id);
                    table.CheckConstraint("CK_TrainingRequirement_OneScope", "(\"OrganizationId\" IS NOT NULL AND \"ServiceSlotId\" IS NULL) OR (\"OrganizationId\" IS NULL AND \"ServiceSlotId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TrainingRequirements_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrainingRequirements_ServiceSlots_ServiceSlotId",
                        column: x => x.ServiceSlotId,
                        principalTable: "ServiceSlots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrainingRequirements_TrainingContents_TrainingContentId",
                        column: x => x.TrainingContentId,
                        principalTable: "TrainingContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinistryId = table.Column<int>(type: "int", nullable: false),
                    HomeTeamId = table.Column<int>(type: "int", nullable: false),
                    AwayTeamId = table.Column<int>(type: "int", nullable: false),
                    ArenaId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: true),
                    AwayScore = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Games_Arenas_ArenaId",
                        column: x => x.ArenaId,
                        principalTable: "Arenas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Games_Ministries_MinistryId",
                        column: x => x.MinistryId,
                        principalTable: "Ministries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Games_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Games_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    JerseyNumber = table.Column<int>(type: "int", nullable: true),
                    Position = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PrimaryContactPersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PrimaryContactPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PrimaryContactEmail = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    JoinedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeftUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_People_PrimaryContactPersonUserId",
                        column: x => x.PrimaryContactPersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Players_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Arenas_OrganizationId_Name",
                table: "Arenas",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_PersonUserId_StartUtc",
                table: "Assignments",
                columns: new[] { "PersonUserId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_ServiceSlotId_StartUtc",
                table: "Assignments",
                columns: new[] { "ServiceSlotId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_ArenaId_StartUtc",
                table: "Games",
                columns: new[] { "ArenaId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_AwayTeamId",
                table: "Games",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_HomeTeamId",
                table: "Games",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_MinistryId_StartUtc",
                table: "Games",
                columns: new[] { "MinistryId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Ministries_CoordinatorPersonUserId",
                table: "Ministries",
                column: "CoordinatorPersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Ministries_OrganizationId_Name",
                table: "Ministries",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ministries_ParentMinistryId",
                table: "Ministries",
                column: "ParentMinistryId");

            migrationBuilder.CreateIndex(
                name: "IX_MinistryInterests_MinistryId",
                table: "MinistryInterests",
                column: "MinistryId");

            migrationBuilder.CreateIndex(
                name: "IX_MinistryInterests_PersonUserId",
                table: "MinistryInterests",
                column: "PersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MinistryInterests_PersonUserId_MinistryId",
                table: "MinistryInterests",
                columns: new[] { "PersonUserId", "MinistryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_OrganizationId",
                table: "OrganizationMemberships",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_PersonUserId_OrganizationId",
                table: "OrganizationMemberships",
                columns: new[] { "PersonUserId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Name",
                table: "Organizations",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_RegistrationToken",
                table: "Organizations",
                column: "RegistrationToken",
                unique: true,
                filter: "[RegistrationToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_People_Email",
                table: "People",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_People_LastName",
                table: "People",
                column: "LastName");

            migrationBuilder.CreateIndex(
                name: "IX_PersonClaimTokens_PersonUserId",
                table: "PersonClaimTokens",
                column: "PersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonClaimTokens_TokenHash",
                table: "PersonClaimTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_PrimaryContactPersonUserId",
                table: "Players",
                column: "PrimaryContactPersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_TeamId",
                table: "Players",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSlots_CoordinatorPersonUserId",
                table: "ServiceSlots",
                column: "CoordinatorPersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSlots_MinistryId_Name",
                table: "ServiceSlots",
                columns: new[] { "MinistryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlotDocuments_ServiceSlotId_Category",
                table: "SlotDocuments",
                columns: new[] { "ServiceSlotId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_SlotDocuments_UploadedByUserId",
                table: "SlotDocuments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SlotOccurrences_ServiceSlotId_StartUtc",
                table: "SlotOccurrences",
                columns: new[] { "ServiceSlotId", "StartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlotOccurrences_StartUtc",
                table: "SlotOccurrences",
                column: "StartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAdminGrantAudits_TimestampUtc",
                table: "SystemAdminGrantAudits",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_CoachPersonUserId",
                table: "Teams",
                column: "CoachPersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_MinistryId_Name",
                table: "Teams",
                columns: new[] { "MinistryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingActivities_PersonUserId_TrainingContentId_TrainingContentVersion",
                table: "TrainingActivities",
                columns: new[] { "PersonUserId", "TrainingContentId", "TrainingContentVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingActivities_TrainingContentId",
                table: "TrainingActivities",
                column: "TrainingContentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCompletions_PersonUserId_TrainingContentId_TrainingContentVersion",
                table: "TrainingCompletions",
                columns: new[] { "PersonUserId", "TrainingContentId", "TrainingContentVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCompletions_TrainingContentId",
                table: "TrainingCompletions",
                column: "TrainingContentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContents_OrganizationId_Title",
                table: "TrainingContents",
                columns: new[] { "OrganizationId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContents_OrganizationId_Title_Version",
                table: "TrainingContents",
                columns: new[] { "OrganizationId", "Title", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingRequirements_OrganizationId",
                table: "TrainingRequirements",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingRequirements_ServiceSlotId",
                table: "TrainingRequirements",
                column: "ServiceSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingRequirements_TrainingContentId",
                table: "TrainingRequirements",
                column: "TrainingContentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessionAttendees_PersonUserId",
                table: "TrainingSessionAttendees",
                column: "PersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessionAttendees_TrainingSessionId_PersonUserId",
                table: "TrainingSessionAttendees",
                columns: new[] { "TrainingSessionId", "PersonUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_OrganizationId_StartUtc",
                table: "TrainingSessions",
                columns: new[] { "OrganizationId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_Status",
                table: "TrainingSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_TrainingContentId",
                table: "TrainingSessions",
                column: "TrainingContentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "MinistryInterests");

            migrationBuilder.DropTable(
                name: "OrganizationMemberships");

            migrationBuilder.DropTable(
                name: "PersonClaimTokens");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "SlotDocuments");

            migrationBuilder.DropTable(
                name: "SlotOccurrences");

            migrationBuilder.DropTable(
                name: "SystemAdminGrantAudits");

            migrationBuilder.DropTable(
                name: "TrainingActivities");

            migrationBuilder.DropTable(
                name: "TrainingCompletions");

            migrationBuilder.DropTable(
                name: "TrainingRequirements");

            migrationBuilder.DropTable(
                name: "TrainingSessionAttendees");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Arenas");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "ServiceSlots");

            migrationBuilder.DropTable(
                name: "TrainingSessions");

            migrationBuilder.DropTable(
                name: "Ministries");

            migrationBuilder.DropTable(
                name: "TrainingContents");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
