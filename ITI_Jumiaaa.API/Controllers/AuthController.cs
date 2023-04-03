﻿using Azure.Core;
using ITI_Jumiaaa.API.Helper;
using ITI_Jumiaaa.API.Models;
using ITI_Jumiaaa.API.ModelsDto;
using ITI_Jumiaaa.DbContext;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NuGet.ProjectModel;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using WebApplication1.Models;

namespace ITI_Jumiaaa.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        #region Fields
        public static User user = new User();
        private readonly IConfiguration configuration;
        private readonly APIContext context;
        private readonly UserManager<ApplicationUser> userManager;
        private readonly JWT _jWT; 
        #endregion

        #region Injection Services
        public AuthController(IConfiguration configuration, APIContext context, UserManager<ApplicationUser> userManager, IOptions<JWT> jWT)
        {
            this.configuration = configuration;
            this.context = context;
            this.userManager = userManager;
            this._jWT = jWT.Value;
        }

        #endregion


        #region Register Action API
        //Register New Account
        [HttpPost("Register")]
        public async Task<IActionResult> RegisterAsync([FromBody] UserDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await RegisterAuth(request);
            if (!result.IsAuthenticated)
                return BadRequest(result.Message);

            // Return Specific Values
            //return Ok(new { Token=result.Token,Expire=result.ExpireOn});

            // Return All values 
            return Ok(result);
        } 
        #endregion

        #region Register Method 
        private async Task<AuthenticationUser> RegisterAuth(UserDto request)
        {
            // Check Email exist on Db or not
            if (await userManager.FindByEmailAsync(request.Email) is not null)
                return new AuthenticationUser { Message = "Email is Already Registered!" };
            // Check UserName exist on Db or not
            if (await userManager.FindByEmailAsync(request.UserName) is not null)
                return new AuthenticationUser { Message = "UserName is Already Registered!" };

            CreatePasswordHash(request.Password, out byte[] passwordhash, out byte[] passwordsalt);

            user.UserName = request.UserName;
            user.PasswordHash = passwordhash;
            user.PasswordSalt = passwordsalt;
            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
           // user.ProfilePicture = request.ProfilePicture;
            user.City = request.City;
            user.FullAddress = request.FullAddress;
            user.GenderId = request.GenderId;
            user.GovernorateId = request.GovernorateId;
            user.PhoneNumber = request.PhoneNumber;
            user.Email = request.Email;

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var errors = string.Empty;
                foreach (var item in result.Errors)
                {
                    errors += $"{item.Description} , ";
                }
                return new AuthenticationUser { Message = errors };

            }
            // Assign Role to User
            await userManager.AddToRoleAsync(user, "User");

            // create Token Key For New USer
            var jwtSecurityToken = await CreateTokenJWTAfterRegister(user);

            return new AuthenticationUser
            {
                Email = user.Email,
                ExpireOn = jwtSecurityToken.ValidTo,
                IsAuthenticated = true,
                RolesList = new List<string> { "User" },
                Token = new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken),
                UserName = user.UserName

            };
        }

        #endregion

        #region Create Token From USer After Register 
        private async Task<JwtSecurityToken> CreateTokenJWTAfterRegister(ApplicationUser user)
        {
            var userClaims = await userManager.GetClaimsAsync(user);
            var roles=await userManager.GetRolesAsync(user);
            var roleClaims = new List<Claim>();
            foreach (var role in roles)
            {
                roleClaims.Add(new Claim("roles", role));
            }

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub,user.UserName),
                new Claim(JwtRegisteredClaimNames.Jti,Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email,user.Email),
                new Claim("uid",user.Id)
            }.Union(userClaims).Union(roleClaims);

            // To Get the Key from AppSetting on Dbs (Token Stored)
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8
                .GetBytes(configuration.GetSection("AppSettings:Token").Value));

            // Check Credntial
            var cred = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var jwtSecurityToken = new JwtSecurityToken
                (
                    issuer:_jWT.Issuer,
                    audience: _jWT.Audience,
                    claims: claims,
                    expires:DateTime.Now.AddDays(_jWT.DurationDays),
                    signingCredentials:cred
                );

            return jwtSecurityToken;
        }
        #endregion


        #region Login Action API
        // Login Account
        [HttpPost("Login")]
        public async Task<ActionResult<string>> LoginAsync([FromBody] UserLogin request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await CheckUserExistOrNot(request);
            if (!result.IsAuthenticated)
                return BadRequest(result.Message);
            return Ok(result);
        }

        #endregion

        #region Validate Username or Password True or Not
        private async Task<AuthenticationUser> CheckUserExistOrNot(UserLogin request)
        {
            AuthenticationUser authModel = new AuthenticationUser();
            var user = await userManager.FindByEmailAsync(request.Email);

            // Check Email exist on Db or not
            if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
                return new AuthenticationUser { Message = "Email or Password is Incorrect!!" };

            var jwtSecurityToken =await CreateTokenJWTAfterRegister(user);

            // If True USername and PAssword 
            authModel.IsAuthenticated = true;
            authModel.Token= CreateToken(user);
            authModel.Email = user.Email;
            authModel.UserName = user.UserName;
            authModel.ExpireOn = jwtSecurityToken.ValidTo;

            // Get Roles of User Login
            var RolesList = await userManager.GetRolesAsync(user);
            authModel.RolesList = RolesList.ToList();

            return authModel;

        } 
        #endregion

        #region Create Token from USer Login
        private string CreateToken(ApplicationUser user)
        {
            List<Claim> claims = new List<Claim>()
            {
                new Claim(ClaimTypes.Name,user.UserName)
            };
            // To Get the Key from AppSetting on Dbs (Token Stored)
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8
                .GetBytes(configuration.GetSection("AppSettings:Token").Value));

            // Check Credntial
            var cred = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.Now.AddDays(1),
                signingCredentials: cred);

            // then get token and return it 
            var jwt = new JwtSecurityTokenHandler().WriteToken(token);

            return jwt;
        }
        #endregion


        #region Create PAssword Hash with Algorithm HMACSHA512
        // Create Password Hash with JWT Json Web Token
        private void CreatePasswordHash(string password, out byte[] passwordhash, out byte[] passwordsalt)
        {
            // create password hash with alogorithm of Cryptography
            using (var hmac = new HMACSHA512())
            {
                passwordsalt = hmac.Key;
                passwordhash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
            }
        }

        #endregion

        #region Verify Password Hash 
        // Verify Password HAsh
        private bool VerifyPasswordHash(string password, byte[] passwordhash, byte[] passwordsalt)
        {
            // create password hash with alogorithm of Cryptography
            using (var hmac = new HMACSHA512(passwordsalt))
            {
                var computedHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                // if login successfully
                return computedHash.SequenceEqual(passwordhash);
            }

        } 
        #endregion
    }
}
