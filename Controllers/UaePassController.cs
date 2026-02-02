using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace GetOtpAPI.Controllers;

[ApiController]
[Route("api/uaepass")]
public class UaePassController : ControllerBase
{
    // Same constants you used
    private const int TIMEOUT = 120;
    private const string IDENTIFIER_INPUT_ID = "username";
    private const string PASSCODE_FIELD = "data-after-content";
    private const string PASSCODE_CARD_DIV_CSS = "div.passcode-card";
    private const string IDENTIFIER_SUBMIT_BTN_ID = "basicPasswordForm-submitButton";
    private const int TIMEOUT_SECONDS = 120;
    

    private readonly AuthCodeStore _store;

    public UaePassController(AuthCodeStore store)
    {
        _store = store;
    }

    // 1) Request passcode (OTP) by identifier (mobile/email)
    [HttpPost("passcode")]
    public ActionResult<PasscodeResponse> GetPasscode()
    {
        //if (string.IsNullOrWhiteSpace(req.Identifier))
        //    return BadRequest("Identifier is required.");

        // In real apps: generate state per request (unique), not hard-coded
        //var state = string.IsNullOrWhiteSpace(req.State)
        //    ? Guid.NewGuid().ToString("N")
        //    : req.State.Trim();

        // redirect_uri must match what UAE PASS expects AND be reachable by UAE PASS
        // If you're running locally, UAE PASS won't reach your localhost callback.
        // For local testing you can still automate passcode, but callback capture won't work unless public URL.
        var redirectUri = "https://tdrauaepasspoc-d8a4gmhxb3hydfea.uaenorth-01.azurewebsites.net/api/callbackFunctioncs"; 
        //req.RedirectUri?.Trim()
        //    ?? $"{Request.Scheme}://{Request.Host}/api/uaepass/callback";

        var passcode = ExecuteIVRLogin();

        return Ok(new PasscodeResponse
        {
            Identifier = "971503424573",
            Passcode = passcode,
            State = "HnlHOJTkTb66Y5H",
            RedirectUri = redirectUri,
            TimeoutSeconds = TIMEOUT
        });
    }

    // 2) UAE PASS redirects back here with ?code=...&state=...
    [HttpGet("callback")]
    public IActionResult Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { message = "UAE PASS returned an error", error });

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return BadRequest(new { message = "Missing code/state", code, state });

        _store.Save(state, code);

        // You can return HTML or just OK
        return Ok(new { message = "Authorization code captured", state });
    }

    // 3) Poll later to get the auth code by state
    [HttpGet("code/{state}")]
    public IActionResult GetCode([FromRoute] string state)
    {
        if (_store.TryGet(state, out var code) && !string.IsNullOrWhiteSpace(code))
            return Ok(new { state, code });

        return NotFound(new { message = "No code captured for this state yet", state });
    }

    #region Playwright

    [HttpPost("GetPasscodePlaywrightTest")]
    public async Task<ActionResult<PasscodeResponse>> GetPasscodePlaywrightTest()
    {
        var redirectUri = "https://tdrauaepasspoc-d8a4gmhxb3hydfea.uaenorth-01.azurewebsites.net/api/callbackFunctioncs";

        // Call the async Playwright logic
        var passcode = await ExecuteIVRLoginAsyncPlaywrightTest();

        return Ok(new PasscodeResponse
        {
            Identifier = "971503424573",
            Passcode = passcode,
            State = "HnlHOJTkTb66Y5H",
            RedirectUri = redirectUri,
            TimeoutSeconds = TIMEOUT_SECONDS
        });
    }

    private async Task<string> ExecuteIVRLoginAsyncPlaywrightTest()
    {
        var redirectUri = "https://tdrauaepasspoc-d8a4gmhxb3hydfea.uaenorth-01.azurewebsites.net/api/callbackFunctioncs";
        var authUrl = "https://stg-id.uaepass.ae/idshub/authorize?" +
                      "response_type=code&" +
                      "client_id=sandbox_stage&" +
                      "scope=urn:uae:digitalid:profile:general&" +
                      "state=HnlHOJTkTb66Y5H&" +
                      $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                      "acr_values=urn:safelayer:tws:policies:authentication:level:low";

        using var playwright = await Playwright.CreateAsync();
        // Launching with headless: true for server-side execution
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-dev-shm-usage" }
        });

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            // 1. Navigate to UAE PASS
            await page.GotoAsync(authUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // 2. Fill Identifier
            await page.WaitForSelectorAsync(IDENTIFIER_INPUT_ID, new PageWaitForSelectorOptions { Timeout = TIMEOUT_SECONDS * 1000 });
            await page.FillAsync(IDENTIFIER_INPUT_ID, "971503424573");

            // 3. Handle the "Loading" overlay
            // FIX: Using .First avoids the 'strict mode violation' where multiple col-sm-12 exist
            var loadingDiv = page.Locator("div.col-sm-12").First;
            try
            {
                await loadingDiv.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Hidden,
                    Timeout = 5000
                });
            }
            catch
            {
                /* If it disappears faster than 5s or never appears, continue */
            }

            // 4. Click Submit
            // Ensure the button is visible and enabled before clicking
            await page.WaitForSelectorAsync(IDENTIFIER_SUBMIT_BTN_ID, new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible });
            await page.ClickAsync(IDENTIFIER_SUBMIT_BTN_ID);

            // 5. Get Passcode from attribute
            var passcodeCard = page.Locator(PASSCODE_CARD_DIV_CSS);
            await passcodeCard.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = TIMEOUT_SECONDS * 1000
            });

            string passcodeValue = await passcodeCard.GetAttributeAsync(PASSCODE_FIELD) ?? "";

            // 6. Optional: Wait for the final redirect (mobile app approval)
            try
            {
                // We give the user 30 seconds to approve on their mobile device
                await page.WaitForURLAsync(url => url.Contains("code="), new PageWaitForURLOptions { Timeout = 30000 });
            }
            catch (System.TimeoutException)
            {
                // Flow continues even if redirect hasn't happened yet, as we have the passcode
            }

            return passcodeValue;
        }
        catch (Exception ex)
        {
            // Log your exception here (e.g., _logger.LogError(ex))
            throw;
        }
        finally
        {
            // Explicitly close context and browser to free up system resources
            await context.CloseAsync();
            await browser.CloseAsync();
        }
    }
    #endregion
    private static string ExecuteIVRLogin()
    {
        var redirectUri = "https://tdrauaepasspoc-d8a4gmhxb3hydfea.uaenorth-01.azurewebsites.net/api/callbackFunctioncs";

        string passcodeValue = "";

        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");

        // If you face issues with ChromeDriver versions, consider using Selenium Manager (newer Selenium),
        // or ensure Chrome + chromedriver versions align.
        using IWebDriver driver = new ChromeDriver(options);

        try
        {
            var url =
                "https://stg-id.uaepass.ae/idshub/authorize?" +
                "response_type=code&" +
                "client_id=sandbox_stage&" +
                "scope=urn:uae:digitalid:profile:general&" +
                $"state=HnlHOJTkTb66Y5H&" +
                $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                "acr_values=urn:safelayer:tws:policies:authentication:level:low";

            driver.Navigate().GoToUrl(url);

            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(TIMEOUT));

            var textField = wait.Until(d => d.FindElement(By.Id(IDENTIFIER_INPUT_ID)));
            textField.Clear();
            textField.SendKeys("971503424573");

            // Wait until the "div.col-sm-12" disappears (same as your logic)
            var shortWait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            shortWait.Until(d =>
            {
                try
                {
                    var element = d.FindElement(By.CssSelector("div.col-sm-12"));
                    return !element.Displayed;
                }
                catch (NoSuchElementException)
                {
                    return true;
                }
            });

            var submitBtn = wait.Until(d => d.FindElement(By.Id(IDENTIFIER_SUBMIT_BTN_ID)));

            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", submitBtn);
            submitBtn.Click();

            var passcodeCard = wait.Until(d => d.FindElement(By.CssSelector(PASSCODE_CARD_DIV_CSS)));
            passcodeValue = passcodeCard.GetAttribute(PASSCODE_FIELD) ?? "";

            // Optional: wait for redirect to happen (means user confirmed in mobile app)
            var beforeRedirect = driver.Url;
            try
            {
                wait.Until(d => !d.Url.Equals(beforeRedirect));
            }
            catch (WebDriverTimeoutException)
            {
                // passcode was generated but redirect didn't happen in time
            }

            return passcodeValue;
        }
        finally
        {
            driver.Quit();
        }
    }
}

public sealed class PasscodeRequest
{
    public string Identifier { get; set; } = "";
    public string? RedirectUri { get; set; }
    public string? State { get; set; }
}

public sealed class PasscodeResponse
{
    public string Identifier { get; set; } = "";
    public string Passcode { get; set; } = "";
    public string State { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public int TimeoutSeconds { get; set; }
}
