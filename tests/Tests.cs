using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public static class Tests
{
    static int passed;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++; Console.WriteLine("PASS " + name);
    }
    static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception ex) {
            Check(ex.GetType().FullName == "Microsoft.Contracts.RequiresException", name + " (" + ex.GetType().Name + ")");
            return;
        }
        throw new Exception("Contract did not reject: " + name);
    }
    static void Core()
    {
        var bank = new BankAccountService(0, 1000000);
        bank.Transfer(2, 1, 1000000);
        Check(bank.GetBalance(1) == 1000000 && bank.GetBalance(2) == 0, "full transfer at bounds");
        bank.Withdraw(1, 1000000); bank.Deposit(2, 1000000);
        Check(bank.GetTotalBalance() == 1000000, "withdraw to zero and deposit to limit");
        Reject(() => new BankAccountService(-1, 0), "negative initial balance");
        Reject(() => new BankAccountService(0, 1000001), "initial balance over limit");
        Reject(() => bank.GetBalance(0), "invalid id");
        Reject(() => bank.Deposit(2, 1), "deposit over limit");
        Reject(() => bank.Deposit(1, 0), "zero deposit");
        Reject(() => bank.Deposit(1, Int32.MaxValue), "Int32.MaxValue deposit");
        Reject(() => bank.Withdraw(1, 1), "overdraft");
        Reject(() => bank.Transfer(2, 2, 1), "self transfer");
        Check(bank.GetBalance(1) == 0 && bank.GetBalance(2) == 1000000, "contract failures preserve state");

        bank = new BankAccountService(1000, 500);
        var model = new long[] { 1000, 500 };
        var random = new Random(20261114);
        for (int n = 0; n < 10000; n++)
        {
            int index = random.Next(2), other = 1 - index, amount = random.Next(1, 10001);
            switch (random.Next(3))
            {
                case 0:
                    if (model[index] + amount <= 1000000) {
                        bank.Deposit(index + 1, amount); model[index] += amount;
                    } break;
                case 1:
                    if (amount <= model[index]) {
                        bank.Withdraw(index + 1, amount); model[index] -= amount;
                    } break;
                case 2:
                    if (amount <= model[index] && model[other] + amount <= 1000000) {
                        bank.Transfer(index + 1, other + 1, amount);
                        model[index] -= amount; model[other] += amount;
                    } break;
            }
            if (bank.GetBalance(1) != model[0] || bank.GetBalance(2) != model[1] ||
                bank.GetTotalBalance() != model.Sum()) throw new Exception("Model mismatch at " + n);
        }
        Check(true, "10000 deterministic steps against independent Int64 model");
    }

    static HttpClient client;
    static async Task<string> Call(string method, string path, string body, int expected, string name)
    {
        using (var request = new HttpRequestMessage(new HttpMethod(method), path))
        {
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using (var response = await client.SendAsync(request))
            {
                string text = await response.Content.ReadAsStringAsync();
                Check((int)response.StatusCode == expected,
                    name + " HTTP " + (int)response.StatusCode + " " + text);
                return text;
            }
        }
    }
    static async Task<int> Balance(int id)
    {
        string text = await client.GetStringAsync("api/account/balance?accountId=" + id);
        return (int)JObject.Parse(text)["balance"];
    }
    static async Task SetBalance(int id, int target)
    {
        int current = await Balance(id);
        if (current == target) return;
        await Call("POST", "api/account/" + (current < target ? "deposit" : "withdraw"),
            "{\"accountId\":" + id + ",\"amount\":" + Math.Abs(target - current) + "}", 200, "prepare balance");
    }
    static async Task Invalid(string path, string body, int status, string name)
    {
        int a = await Balance(1), b = await Balance(2);
        string response = await Call("POST", "api/account/" + path, body, status, name);
        Check(JObject.Parse(response)["error"] != null && await Balance(1) == a && await Balance(2) == b,
            name + " error code and unchanged balances");
    }
    static async Task Http(string address)
    {
        client = new HttpClient(new HttpClientHandler { UseProxy = false });
        client.BaseAddress = new Uri(address.TrimEnd('/') + "/");
        await SetBalance(1, 1000); await SetBalance(2, 500);
        await Call("GET", "api/account/balance?accountId=1", null, 200, "read balance");
        await Call("GET", "api/account/balance", null, 400, "missing query");
        await Call("GET", "api/account/balance?accountId=abc", null, 400, "invalid query");
        await Call("GET", "api/account/balance?accountId=3", null, 404, "unknown account");
        await Call("GET", "api/account/deposit", null, 405, "wrong HTTP method");
        await Call("GET", "api/unknown", null, 404, "unknown route");
        await Call("POST", "api/account/deposit", "{\"accountId\":1,\"amount\":200}", 200, "deposit");
        Check(await Balance(1) == 1200 && await Balance(2) == 500, "deposit exact delta and frame");
        await Call("POST", "api/account/withdraw", "{\"accountId\":1,\"amount\":100}", 200, "withdraw");
        Check(await Balance(1) == 1100 && await Balance(2) == 500, "withdraw exact delta and frame");
        await Call("POST", "api/account/transfer", "{\"fromAccountId\":1,\"toAccountId\":2,\"amount\":300}", 200, "transfer");
        Check(await Balance(1) == 800 && await Balance(2) == 800, "transfer conservation and exact deltas");
        foreach (string body in new[] {
            "null", "{}", "[]", "{", "{\"accountId\":1}",
            "{\"accountId\":1,\"amount\":0}", "{\"accountId\":1,\"amount\":-1}",
            "{\"accountId\":1,\"amount\":1.5}", "{\"accountId\":1,\"amount\":\"1\"}",
            "{\"accountId\":1,\"amount\":2147483648}", "{\"accountId\":1,\"amount\":null}",
            "{\"accountId\":1,\"amount\":1,\"extra\":1}"
        }) await Invalid("deposit", body, 400, "invalid JSON input " + body);
        await Invalid("withdraw", "{\"accountId\":1,\"amount\":801}", 409, "overdraft");
        await Invalid("deposit", "{\"accountId\":1,\"amount\":2147483647}", 409, "huge positive amount");
        await Invalid("deposit", "{\"accountId\":3,\"amount\":1}", 404, "unknown deposit account");
        await Invalid("transfer", "{\"fromAccountId\":1,\"toAccountId\":1,\"amount\":1}", 400, "self transfer");
        await Invalid("transfer", "{\"fromAccountId\":1,\"toAccountId\":3,\"amount\":1}", 404, "unknown transfer target");
        await Invalid("transfer", "{\"fromAccountId\":1,\"toAccountId\":2,\"amount\":801}", 409, "transfer overdraft");
        await SetBalance(2, 1000000);
        await Invalid("transfer", "{\"fromAccountId\":1,\"toAccountId\":2,\"amount\":1}", 409, "receiver limit preserves sender");
        await Invalid("deposit", "{\"accountId\":2,\"amount\":1}", 409, "deposit at maximum");
        await SetBalance(1, 0); await SetBalance(2, 1000000);
        await Call("POST", "api/account/transfer", "{\"fromAccountId\":2,\"toAccountId\":1,\"amount\":1000000}", 200, "full boundary transfer");
        Check(await Balance(1) == 1000000 && await Balance(2) == 0, "boundary transfer results");
        await SetBalance(1, 1000); await SetBalance(2, 500);
        // Read-check-write and response snapshot must share the same lock.
        var pending = Enumerable.Range(0, 100).Select(async n => {
            using (var response = await client.PostAsync("api/account/deposit",
                new StringContent("{\"accountId\":1,\"amount\":1}", Encoding.UTF8, "application/json")))
                if ((int)response.StatusCode != 200) throw new Exception("Concurrent deposit failed");
        }).ToArray();
        await Task.WhenAll(pending);
        Check(await Balance(1) == 1100 && await Balance(2) == 500, "100 concurrent deposits without lost updates");
        var moves = Enumerable.Range(0, 100).Select(async n => {
            int from = n % 2 + 1, to = 3 - from;
            using (var response = await client.PostAsync("api/account/transfer", new StringContent(
                "{\"fromAccountId\":" + from + ",\"toAccountId\":" + to + ",\"amount\":1}", Encoding.UTF8, "application/json")))
                if ((int)response.StatusCode != 200) throw new Exception("Concurrent transfer failed");
        }).ToArray();
        await Task.WhenAll(moves);
        Check(await Balance(1) == 1100 && await Balance(2) == 500, "100 concurrent transfers conserve balances");
        await SetBalance(1, 1000); await SetBalance(2, 500);
        client.Dispose();
    }
    public static int Main(string[] args)
    {
        try {
            if (args.Length == 0) Core(); else Http(args[0]).GetAwaiter().GetResult();
            Console.WriteLine("RESULT: " + passed + " checks passed, 0 failed"); return 0;
        } catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
}
