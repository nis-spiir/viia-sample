using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ViiaSample.Data;
using ViiaSample.Models;
using ViiaSample.Services;

namespace ViiaSample.Controllers
{
    [Route("viia")]
    [Authorize]
    public class ViiaController : Controller
    {
        private readonly IViiaService _ViiaService;
        private readonly ApplicationDbContext _dbContext;

        public ViiaController(IViiaService ViiaService, ApplicationDbContext dbContext)
        {
            _ViiaService = ViiaService;
            _dbContext = dbContext;
        }

        // Web hook for Viia to push data
        [HttpGet("data/{userId}")]
        [AllowAnonymous]
        public IActionResult DataCallback(string userId)
        {
            // Notify the user?
            return View("GenericViewWithPostMessageOnLoad");
        }

        [HttpPost("update")]
        public async Task<IActionResult> RequestDataUpdate()
        {
            var dataUpdateResponse = await _ViiaService.InitiateDataUpdate(User);
            return Ok(new
                {authUrl = dataUpdateResponse.Status == UpdateStatus.AllQueued ? string.Empty : dataUpdateResponse.AuthUrl});
        }

        [HttpGet("login")]
        public IActionResult Login()
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            var user = _dbContext.Users.FirstOrDefault(x => x.Id == currentUserId);
            var ViiaUrl = _ViiaService.GetAuthUri(User, user?.Email);

            return Redirect(ViiaUrl.ToString());
        }

        [HttpGet("callback")]
        public async Task<IActionResult> LoginCallback([FromQuery] string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return BadRequest();
            }

            var tokenResponse = await _ViiaService.ExchangeCodeForAccessToken(code);
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            var user = _dbContext.Users.FirstOrDefault(x => x.Id == currentUserId);
            if (user == null)
            {
                return Unauthorized();
            }

            user.ViiaAccessToken = tokenResponse.AccessToken;
            user.ViiaTokenType = tokenResponse.TokenType;
            user.ViiaRefreshToken = tokenResponse.RefreshToken;
            user.ViiaAccessTokenExpires = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            _dbContext.Users.Update(user);
            await _dbContext.SaveChangesAsync();

            return View("GenericViewWithPostMessageOnLoad", Request.QueryString.Value);
        }

        [HttpGet("accounts")]
        public async Task<IActionResult> Accounts()
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            var user = _dbContext.Users.FirstOrDefault(x => x.Id == currentUserId);
            if (user?.ViiaAccessToken == null || user.ViiaAccessTokenExpires < DateTimeOffset.UtcNow)
            {
                return View(new AccountViewModel
                {
                    AccountsGroupedByProvider = null,
                    ViiaConnectUrl = _ViiaService.GetAuthUri(User, user?.Email).ToString()
                });
            }

            var accounts = await _ViiaService.GetUserAccounts(User);
            var groupedAccounts = accounts.ToLookup(x => x.Provider?.Id, x => x);

            var model = new AccountViewModel
            {
                AccountsGroupedByProvider = groupedAccounts,
                ViiaConnectUrl = _ViiaService.GetAuthUri(User, user.Email).ToString(),
                JwtToken = new JwtSecurityTokenHandler().ReadJwtToken(user.ViiaAccessToken),
                RefreshToken = new JwtSecurityTokenHandler().ReadJwtToken(user.ViiaRefreshToken)
            };
            return View(model);
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> Transactions([FromQuery] string accountId)
        {
            var transactions = await _ViiaService.GetAccountTransactions(User, accountId);
            return View(transactions);
        }
    }
}