using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_ProductId_LocationId",
                table: "Stocks");

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "Stocks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "StockMovementLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBatchTracked",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OutputBatchId",
                table: "ProductionOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "ProductionOrderMaterials",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Batches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_BatchId",
                table: "Stocks",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProductId_LocationId_BatchId",
                table: "Stocks",
                columns: new[] { "ProductId", "LocationId", "BatchId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovementLines_BatchId",
                table: "StockMovementLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_OutputBatchId",
                table: "ProductionOrders",
                column: "OutputBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderMaterials_BatchId",
                table: "ProductionOrderMaterials",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ExpiryDate",
                table: "Batches",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProductId_Number",
                table: "Batches",
                columns: new[] { "ProductId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProductionOrderId",
                table: "Batches",
                column: "ProductionOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrderMaterials_Batches_BatchId",
                table: "ProductionOrderMaterials",
                column: "BatchId",
                principalTable: "Batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrders_Batches_OutputBatchId",
                table: "ProductionOrders",
                column: "OutputBatchId",
                principalTable: "Batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovementLines_Batches_BatchId",
                table: "StockMovementLines",
                column: "BatchId",
                principalTable: "Batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Batches_BatchId",
                table: "Stocks",
                column: "BatchId",
                principalTable: "Batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrderMaterials_Batches_BatchId",
                table: "ProductionOrderMaterials");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrders_Batches_OutputBatchId",
                table: "ProductionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovementLines_Batches_BatchId",
                table: "StockMovementLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Batches_BatchId",
                table: "Stocks");

            migrationBuilder.DropTable(
                name: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_BatchId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_ProductId_LocationId_BatchId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_StockMovementLines_BatchId",
                table: "StockMovementLines");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_OutputBatchId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrderMaterials_BatchId",
                table: "ProductionOrderMaterials");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "StockMovementLines");

            migrationBuilder.DropColumn(
                name: "IsBatchTracked",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OutputBatchId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "ProductionOrderMaterials");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProductId_LocationId",
                table: "Stocks",
                columns: new[] { "ProductId", "LocationId" },
                unique: true);
        }
    }
}
