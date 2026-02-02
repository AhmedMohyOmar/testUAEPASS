using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;

namespace GetOtpAPI.Controllers;

[ApiController]
[Route("api/uaepass")]
public class playwrightController : ControllerBase
{
    private const int TIMEOUT_SECONDS = 120;
    private const string IDENTIFIER_INPUT_ID = "#username"; // ID selector
    private const string PASSCODE_FIELD = "data-after-content";
    private const string PASSCODE_CARD_DIV_CSS = "div.passcode-card";
    private const string IDENTIFIER_SUBMIT_BTN_ID = "#basicPasswordForm-submitButton";

    private readonly AuthCodeStore _store;

    public playwrightController(AuthCodeStore store)
    {
        _store = store;
    }

    [HttpPost("GetPasscodePlaywright")]
    public async Task<ActionResult<PasscodeResponse>> GetPasscodePlaywright()
    {
        var redirectUri = "https://tdrauaepasspoc-d8a4gmhxb3hydfea.uaenorth-01.azurewebsites.net/api/callbackFunctioncs";

        // Call the async Playwright logic
        var passcode = await ExecuteIVRLoginAsyncPlaywright();

        return Ok(new PasscodeResponse
        {
            Identifier = "971503424573",
            Passcode = passcode,
            State = "HnlHOJTkTb66Y5H",
            RedirectUri = redirectUri,
            TimeoutSeconds = TIMEOUT_SECONDS
        });
    }

    private async Task<string> ExecuteIVRLoginAsyncPlaywright()
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
}