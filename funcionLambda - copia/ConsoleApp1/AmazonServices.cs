using Amazon.Lambda.Core;
using FikaAmazonAPI;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Feeds;
using FikaAmazonAPI.ConstructFeed;
using FikaAmazonAPI.ConstructFeed.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static FikaAmazonAPI.Utils.Constants;

namespace FuncionLambda
{
    public class AmazonServices
    {
        AmazonConnection amazonConnection;
        ILambdaContext ctx;
        public AmazonServices(AmazonConnection amazonConnection, ILambdaContext ctx)
        {
            this.amazonConnection = amazonConnection;
            this.ctx = ctx;
        }

        public async Task SubmitFeedPRICING_JSONAsync(List<ClientItem> items)
        {
            try
            {
                ConstructJSONFeedService createDocument = new ConstructJSONFeedService(amazonConnection.GetCurrentSellerID);

                var list = new List<PriceMessage>();

                foreach (var item in items)
                {

                    var msg = new PriceMessage()
                    {
                        SKU = item.Sku,
                        StandardPrice = new StandardPrice()
                        {
                            currency = amazonConnection.GetCurrentMarketplace.CurrencyCode.ToString(),
                            Value = decimal.Round(item.Price, 2)
                        }
                    };

                    if (item.maxPrice != null)
                    {
                        msg.MaximumSellerAllowedPrice = new StandardPrice()
                        {
                            currency = amazonConnection.GetCurrentMarketplace.CurrencyCode.ToString(),
                            Value = decimal.Round(item.maxPrice.Value, 2),
                            start_at = DateTime.Now.ToString("yyyy-MM-dd"),
                            end_at = DateTime.Now.AddYears(1).ToString("yyyy-MM-dd"),
                        };
                    }

                    if (item.minPrice != null)
                    {
                        msg.MinimumSellerAllowedPrice = new StandardPrice()
                        {
                            currency = amazonConnection.GetCurrentMarketplace.CurrencyCode.ToString(),
                            Value = decimal.Round(item.minPrice.Value, 2),
                            start_at = DateTime.Now.ToString("yyyy-MM-dd"),
                            end_at = DateTime.Now.AddYears(1).ToString("yyyy-MM-dd"),
                        };
                    }

                    list.Add(msg);
                }

                createDocument.AddPriceMessage(list);

                var jsonString = createDocument.GetJSON();

                ctx.Logger.LogLine($"Submitted pricing feed: {jsonString}");

                /*
                if (markets.Count <= 0)
                    markets.Add("A1RKKUPIHCS9HS");
                */
                string feedID = await amazonConnection.Feed.SubmitFeedAsync(jsonString, FeedType.JSON_LISTINGS_FEED, null, null, ContentType.JSON);


                await GetJsonFeedDetails(amazonConnection, feedID);
            }
            catch (Exception ex)
            {
                ctx.Logger.LogLine($"Error submitting pricing feed: {ex.Message}");
                throw;

            }
        }
        public async Task SubmitInventoryJSON_Async(List<ClientItem> items)
        {
            try
            {
                ConstructJSONFeedService createDocument = new ConstructJSONFeedService(amazonConnection.GetCurrentSellerID);

                var list = new List<InventoryMessage>();

                foreach (var item in items)
                {
                    var msg = new InventoryMessage()
                    {
                        SKU = item.Sku,
                        Quantity = item.Stock,
                        FulfillmentLatency = item.leadTimeToShip.HasValue ? item.leadTimeToShip.Value.ToString() : null,
                        RestockDate = item.restockDate
                    };

                    list.Add(msg);
                }

                createDocument.AddInventoryMessage(list);

                var jsonString = createDocument.GetJSON();

                ctx.Logger.LogLine($"Submitted stock feed: {jsonString}");

                string feedID = await amazonConnection.Feed.SubmitFeedAsync(jsonString, FeedType.JSON_LISTINGS_FEED, null, null, ContentType.JSON);

                await GetJsonFeedDetails(amazonConnection, feedID);
            }
            catch (Exception ex)
            {
                ctx.Logger.LogLine($"Error submitting pricing feed: {ex.Message}");
                throw;

            }
        }
        private async Task GetJsonFeedDetails(AmazonConnection amazonConnection, string feedID)
        {
            string resultFeedDocumentId = string.Empty;
            string reportResult = string.Empty;

            while (string.IsNullOrEmpty(resultFeedDocumentId))
            {
                Feed feedOutput = amazonConnection.Feed.GetFeed(feedID);
                if (feedOutput.ProcessingStatus == Feed.ProcessingStatusEnum.DONE)
                {
                    FeedDocument output = amazonConnection.Feed.GetFeedDocument(feedOutput.ResultFeedDocumentId);
                    reportResult = await amazonConnection.Feed.GetJsonFeedDocumentProcessingReportAsync(output);
                    ctx.Logger.LogLine(reportResult);
                }

                if (!(feedOutput.ProcessingStatus == Feed.ProcessingStatusEnum.INPROGRESS ||

                    feedOutput.ProcessingStatus == Feed.ProcessingStatusEnum.INQUEUE))
                {
                    break;
                }
                else
                {
                    Thread.Sleep(3000);
                }
            }
        }



    }
}
