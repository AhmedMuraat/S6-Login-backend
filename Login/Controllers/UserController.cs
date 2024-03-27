using Login.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using MusicApii.Handlers;
using MusicApii.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Login.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly NetflixLoginContext _dbContext;
        private readonly JWTSettings _jwtSettings;

        public UserController(NetflixLoginContext datacontext, IOptions<JWTSettings> jwtSettings)
        {
            _dbContext = datacontext;
            _jwtSettings = jwtSettings.Value;
        }
        [HttpGet("GetUsers")]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers()
        {
            return await _dbContext.Users.Include(r => r.Role).ToListAsync();
        }

        // GET: api/Users/5
        [HttpGet("GetUser/{id}")]
        public async Task<ActionResult<User>> GetUser(int id)
        {
            var user = await _dbContext.Users.Include(r => r.Role).FirstOrDefaultAsync(x => x.Id == id);



            if (user == null)
            {
                return NotFound();
            }

            return user;
        }

        // GET: api/Users/5
        [HttpPost("/LoginAdmin")]
        /*public async Task<ActionResult<TblUser>> LoginAdmin([FromBody] TblUser user)
        {
           

             var user1 = await _musicAppContext.TblUsers.Include(u => u.Role)
                                        .Where(u => u.Email == user.Email).FirstOrDefaultAsync();

            if (role != "Artist")
            {
                throw new Exception("You dont have Authorization for this");
            }
            else
            {
                if (Password.controlHash(password, user.Password))
                {
                    return user;
                }
                else
                {
                    return null;
                }

                UserWithToken userWithToken = null;

                if (user != null)
                {
                    TblRefreshToken refreshToken = GenerateRefreshToken();
                    user.TblRefreshTokens.Add(refreshToken);
                    await _musicAppContext.SaveChangesAsync();
                

                    userWithToken = new UserWithToken(user);
                    userWithToken.RefreshToken = refreshToken.Token;
                }

                if (userWithToken == null)
                {
                    return NotFound();
                }

                //sign your token here here..
                userWithToken.AccessToken = GenerateAccessToken(user.Id);
                return userWithToken;
            }
        }*/



        [HttpPost("/LoginUser")]
        public async Task<ActionResult<User>> LoginUser([FromBody] User user)
        {

            var user1 = await _dbContext.Users.Where(u => u.Email == user.Email).FirstOrDefaultAsync();

            if (user == null || !Password.controlHash(user.Password, user1.Password))
                throw new Exception("Username or password is incorrect");

            UserWithToken userWithToken = null;

            if (user1 != null)
            {
                RefreshToken refreshToken = GenerateRefreshToken();
                user1.RefreshTokens.Add(refreshToken);
                await _dbContext.SaveChangesAsync();


                userWithToken = new UserWithToken(user1);
                userWithToken.RefreshToken = refreshToken.Token;
            }

            if (userWithToken == null)
            {
                return NotFound();
            }

            //sign your token here here..
            userWithToken.AccessToken = GenerateAccessToken(user1.Id);
            return userWithToken;
        }


        //[HttpPost("Login")]
        //public async Task<ActionResult<UserWithToken>> Login([FromBody] TblUser user)
        //{
        //    user = await _musicAppContext.TblUsers.Include(u => u.Role)
        //                                .Where(u => u.Email == user.Email
        //                                        && u.Password == user.Password).FirstOrDefaultAsync();

        //    UserWithToken userWithToken = null;

        //    if (user != null)
        //    {
        //        TblRefreshToken refreshToken = GenerateRefreshToken();
        //        user.TblRefreshTokens.Add(refreshToken);
        //        await _musicAppContext.SaveChangesAsync();

        //        userWithToken = new UserWithToken(user);
        //        userWithToken.RefreshToken = refreshToken.Token;
        //    }

        //    if (userWithToken == null)
        //    {
        //        return NotFound();
        //    }

        //    //sign your token here here..
        //    userWithToken.AccessToken = GenerateAccessToken(user.Id);
        //    return userWithToken;
        //}

        // POST: api/Users
        [HttpPost("RegisterUser{username}&{email}&{password}&{role}")]
        public async Task<ActionResult<UserWithToken>> RegisterUser(string username, string email, string password, short role)
        {

            var hashedpassword = Password.hashPassword(password);
            User newuser = new User();
            newuser.Username = username;
            newuser.Email = email;
            newuser.Password = hashedpassword;
            newuser.RoleId = role;

            _dbContext.Users.Add(newuser);
            await _dbContext.SaveChangesAsync();

            //load role for registered user
            var user = await _dbContext.Users.Include(u => u.Role)
                                         .Where(u => u.Id == newuser.Id).FirstOrDefaultAsync();

            UserWithToken userWithToken = null;

            if (user != null)
            {
                RefreshToken refreshToken = GenerateRefreshToken();
                user.RefreshTokens.Add(refreshToken);
                await _dbContext.SaveChangesAsync();

                userWithToken = new UserWithToken(user);
                userWithToken.RefreshToken = refreshToken.Token;
            }

            if (userWithToken == null)
            {
                return NotFound();
            }

            //sign your token here here..
            userWithToken.AccessToken = GenerateAccessToken(user.Id);
            return userWithToken;
        }

        // GET: api/Users
        [HttpPost("RefreshToken")]
        public async Task<ActionResult<UserWithToken>> RefreshToken([FromBody] RefreshRequest refreshRequest)
        {
            User user = await GetUserFromAccessToken(refreshRequest.AccessToken);

            if (user != null && ValidateRefreshToken(user, refreshRequest.RefreshToken))
            {
                UserWithToken userWithToken = new UserWithToken(user);
                userWithToken.AccessToken = GenerateAccessToken(user.Id);

                return userWithToken;
            }

            return null;
        }

        // GET: api/Users
        [HttpPost("GetUserByAccessToken")]
        public async Task<ActionResult<User>> GetUserByAccessToken([FromBody] string accessToken)
        {
            User user = await GetUserFromAccessToken(accessToken);

            if (user != null)
            {
                return user;
            }
            return null;
        }

        private bool ValidateRefreshToken(User user, string refreshToken)
        {

            RefreshToken refreshTokenUser = _dbContext.RefreshTokens.Where(rt => rt.Token == refreshToken)
                                                .OrderByDescending(rt => rt.ExpiryDate)
                                                .FirstOrDefault();

            if (refreshTokenUser != null && refreshTokenUser.UserId == user.Id
                && refreshTokenUser.ExpiryDate > DateTime.UtcNow)
            {
                return true;
            }

            return false;
        }

        private async Task<User> GetUserFromAccessToken(string accessToken)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_jwtSettings.SecretKey);

                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false
                };

                SecurityToken securityToken;
                var principle = tokenHandler.ValidateToken(accessToken, tokenValidationParameters, out securityToken);

                JwtSecurityToken jwtSecurityToken = securityToken as JwtSecurityToken;

                if (jwtSecurityToken != null && jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    var userId = principle.FindFirst(ClaimTypes.Name)?.Value;

                    return await _dbContext.Users.Include(u => u.Role)
                                        .Where(u => u.Id == Convert.ToInt32(userId)).FirstOrDefaultAsync();
                }
            }
            catch (Exception)
            {
                return new User();
            }
            return new User();
        }

        private RefreshToken GenerateRefreshToken()
        {
            RefreshToken refreshToken = new RefreshToken();

            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                refreshToken.Token = Convert.ToBase64String(randomNumber);
            }
            refreshToken.ExpiryDate = DateTime.UtcNow.AddMonths(6);

            return refreshToken;
        }

        private string GenerateAccessToken(int userId)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwtSettings.SecretKey);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Name, Convert.ToString(userId))
                }),
                Expires = DateTime.UtcNow.AddDays(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }



        // POST: api/Users
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for
        // more details see https://aka.ms/RazorPagesCRUD.
        [HttpPost("CreateUser")]
        public async Task<ActionResult<User>> PostUser(User user)
        {
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction("GetUser", new { id = user.Id }, user);
        }

        // DELETE: api/Users/5
        [HttpDelete("DeleteUser/{id}")]
        public async Task<ActionResult<User>> DeleteUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();

            return user;
        }

        private bool UserExists(int id)
        {
            return _dbContext.Users.Any(e => e.Id == id);
        }

    }
}
