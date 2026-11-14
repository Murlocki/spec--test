using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BankVerification
{
    public static class BankApi
    {
        // The legacy Spec# runtime associates ownership with the creating thread.
        // All reads, validation against state, writes and snapshots run on one owner.
        private static readonly BlockingCollection<Action> Work = new BlockingCollection<Action>();
        internal static BankAccountService Service;
        static BankApi()
        {
            var owner = new Thread(delegate() {
                Service = new BankAccountService(1000, 500);
                foreach (Action action in Work.GetConsumingEnumerable()) action();
            });
            owner.IsBackground = true;
            owner.Name = "SpecSharp bank owner";
            owner.Start();
        }
        internal static HttpResponseMessage Execute(Func<HttpResponseMessage> action)
        {
            var completion = new TaskCompletionSource<HttpResponseMessage>();
            Work.Add(delegate {
                try { completion.SetResult(action()); }
                catch (Exception ex) { completion.SetException(ex); }
            });
            return completion.Task.GetAwaiter().GetResult();
        }

        public static void Configure(HttpConfiguration configuration)
        {
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Routes.MapHttpRoute("balance", "api/account/balance",
                new { controller = "Account", action = "Balance" });
            configuration.Routes.MapHttpRoute("deposit", "api/account/deposit",
                new { controller = "Account", action = "Deposit" });
            configuration.Routes.MapHttpRoute("withdraw", "api/account/withdraw",
                new { controller = "Account", action = "Withdraw" });
            configuration.Routes.MapHttpRoute("transfer", "api/account/transfer",
                new { controller = "Account", action = "Transfer" });
        }
    }

    public sealed class AccountController : ApiController
    {
        private HttpResponseMessage Error(HttpStatusCode status, string code)
        { return Request.CreateResponse(status, new { error = code }); }

        private HttpResponseMessage Snapshot()
        {
            return Request.CreateResponse(HttpStatusCode.OK, new {
                accounts = new[] {
                    new { accountId = 1, balance = BankApi.Service.GetBalance(1) },
                    new { accountId = 2, balance = BankApi.Service.GetBalance(2) }
                }, totalBalance = BankApi.Service.GetTotalBalance(), unit = "minor"
            });
        }

        // Reject coercion, missing/unknown fields, fractional and out-of-range numbers.
        private bool Read(JToken body, string[] names, out int[] values)
        {
            values = null;
            JObject obj = body as JObject;
            if (!ModelState.IsValid || obj == null || obj.Count != names.Length ||
                obj.Properties().Any(p => !names.Contains(p.Name))) return false;
            int[] parsed = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                JToken token = obj[names[i]];
                if (token == null || token.Type != JTokenType.Integer ||
                    !Int32.TryParse(token.ToString(), out parsed[i])) return false;
            }
            values = parsed;
            return true;
        }

        private bool Exists(int id) { return id == 1 || id == 2; }

        [HttpGet]
        public HttpResponseMessage Balance(int? accountId = null)
        {
            if (!ModelState.IsValid || !accountId.HasValue)
                return Error(HttpStatusCode.BadRequest, "invalid_account_id");
            if (!Exists(accountId.Value)) return Error(HttpStatusCode.NotFound, "account_not_found");
            return BankApi.Execute(delegate {
                return Request.CreateResponse(HttpStatusCode.OK, new {
                    accountId = accountId.Value,
                    balance = BankApi.Service.GetBalance(accountId.Value), unit = "minor"
                });
            });
        }

        [HttpPost]
        public HttpResponseMessage Deposit([FromBody] JToken body)
        { return Change(body, true); }

        [HttpPost]
        public HttpResponseMessage Withdraw([FromBody] JToken body)
        { return Change(body, false); }

        private HttpResponseMessage Change(JToken body, bool deposit)
        {
            int[] v;
            if (!Read(body, new[] { "accountId", "amount" }, out v) || v[1] <= 0)
                return Error(HttpStatusCode.BadRequest, "invalid_request");
            if (!Exists(v[0])) return Error(HttpStatusCode.NotFound, "account_not_found");
            return BankApi.Execute(delegate {
                int balance = BankApi.Service.GetBalance(v[0]);
                if (deposit && v[1] > BankAccountService.MaxBalance - balance)
                    return Error(HttpStatusCode.Conflict, "balance_limit");
                if (!deposit && v[1] > balance)
                    return Error(HttpStatusCode.Conflict, "insufficient_funds");
                if (deposit) BankApi.Service.Deposit(v[0], v[1]);
                else BankApi.Service.Withdraw(v[0], v[1]);
                return Snapshot();
            });
        }

        [HttpPost]
        public HttpResponseMessage Transfer([FromBody] JToken body)
        {
            int[] v;
            if (!Read(body, new[] { "fromAccountId", "toAccountId", "amount" }, out v) ||
                v[2] <= 0 || v[0] == v[1])
                return Error(HttpStatusCode.BadRequest, "invalid_request");
            if (!Exists(v[0]) || !Exists(v[1]))
                return Error(HttpStatusCode.NotFound, "account_not_found");
            return BankApi.Execute(delegate {
                if (v[2] > BankApi.Service.GetBalance(v[0]))
                    return Error(HttpStatusCode.Conflict, "insufficient_funds");
                if (v[2] > BankAccountService.MaxBalance - BankApi.Service.GetBalance(v[1]))
                    return Error(HttpStatusCode.Conflict, "balance_limit");
                BankApi.Service.Transfer(v[0], v[1], v[2]);
                return Snapshot();
            });
        }
    }
}
