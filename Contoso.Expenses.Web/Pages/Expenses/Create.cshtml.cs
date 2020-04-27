﻿using Contoso.Expenses.Common.Models;
using Contoso.Expenses.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using NATS.Client;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Contoso.Expenses.Web.Pages.Expenses
{
    public class CreateModel : PageModel
    {
        private readonly ContosoExpensesWebContext _context;
        private string costCenterAPIUrl;
        private readonly QueueInfo _queueInfo;

        public CreateModel(ContosoExpensesWebContext context, IOptions<ConfigValues> config, QueueInfo queueInfo)
        {
            _context = context;
            costCenterAPIUrl = config.Value.CostCenterAPIUrl;
            _queueInfo = queueInfo;
        }

        public IActionResult OnGet()
        {
            return Page();
        }

        [BindProperty]
        public Expense Expense { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Look up cost center
            CostCenter costCenter = await GetCostCenterAsync(costCenterAPIUrl, Expense.SubmitterEmail);
            if (costCenter != null)
            {
                Expense.CostCenter = costCenter.CostCenterName;
                Expense.ApproverEmail = costCenter.ApproverEmail;
            }
            else
            {
                Expense.CostCenter = "Unkown";
                Expense.ApproverEmail = "Unknown";
            }

            // Write to DB, but don't wait right now
            _context.Expense.Add(Expense);
            Task t = _context.SaveChangesAsync();

            // Serialize the expense and write it to the Azure Storage Queue
            //CloudStorageAccount storageAccount = CloudStorageAccount.Parse(_queueInfo.ConnectionString);
            //CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            //CloudQueue queue = queueClient.GetQueueReference(_queueInfo.QueueName);
            //await queue.CreateIfNotExistsAsync();
            //CloudQueueMessage queueMessage = new CloudQueueMessage(JsonConvert.SerializeObject(Expense));
            //await queue.AddMessageAsync(queueMessage);

            // Serialize the expense and write it to the NATS Queue
            var cf = new ConnectionFactory();
            using (var c = cf.CreateConnection(_queueInfo.ConnectionString))
            {
                Console.WriteLine($"Sending POST {JsonConvert.SerializeObject(Expense)}");
                c.Publish(_queueInfo.QueueName, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Expense)));
            }

            // Ensure the DB write is complete
            t.Wait();

            return RedirectToPage("./Index");
        }

        private static async Task<CostCenter> GetCostCenterAsync(string apiBaseURL, string email)
        {
            string requestUri = "api/costcenter" + "/" + email;

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(apiBaseURL);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage httpResponse = await client.GetAsync(requestUri);

                if (httpResponse.IsSuccessStatusCode)
                {
                    CostCenter costCenter = await httpResponse.Content.ReadAsAsync<CostCenter>();
                    if (costCenter != null)
                        Console.WriteLine("SubmitterEmail: {0} \r\n ApproverEmail: {1} \r\n CostCenterName: {2}",
                            costCenter.SubmitterEmail, costCenter.ApproverEmail, costCenter.CostCenterName);
                    return costCenter;
                }
                else
                {
                    Console.WriteLine("Internal server error: " + httpResponse.StatusCode);
                    return null;
                }
            }
        }
    }
}