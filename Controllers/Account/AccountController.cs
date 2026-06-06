using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using POSERP.Models.DataBase;
using POSERP.Models.Account;

namespace POSERP.Controllers.Account
{
    public class AccountController : Controller
    {
        private readonly Db _db;

        public AccountController(Db db)
        {
            _db = db;
        }

        // ================= LOGIN =================
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();

                string query = @"SELECT UserID, FullName, Email, Role, PasswordHash
                         FROM Users
                         WHERE Email=@Email AND Role=@Role AND Status='Active'";

                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@Email", model.Email);
                cmd.Parameters.AddWithValue("@Role", model.Role);

                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    if (dr.Read())
                    {
                        string storedPassword = dr["PasswordHash"].ToString();

                        // ⚠️ CURRENT (not secure but matches your DB)
                        if (model.Password == storedPassword)
                        {
                            string userId = dr["UserID"].ToString();
                            string fullName = dr["FullName"].ToString();
                            string role = dr["Role"].ToString();

                            var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, model.Email),
                        new Claim(ClaimTypes.Email, model.Email),
                        new Claim(ClaimTypes.GivenName, fullName),
                        new Claim(ClaimTypes.Role, role),
                        new Claim("UserID", userId)
                    };

                            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                            var principal = new ClaimsPrincipal(identity);

                            await HttpContext.SignInAsync(principal);

                            if (role == "Admin")
                                return RedirectToAction("Dashboard", "Admin");

                            if (role == "Manager")
                                return RedirectToAction("Index", "Manager");

                            if (role == "Cashier")
                                return RedirectToAction("Dashboard", "Cashier");
                        }
                    }
                }
            }

            ModelState.AddModelError("", "Invalid email, password, or role.");
            return View(model);
        }

        // ================= REGISTER =================
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();

                // Check if email exists
                string checkQuery = "SELECT COUNT(*) FROM Users WHERE Email=@Email";
                SqlCommand checkCmd = new SqlCommand(checkQuery, con);
                checkCmd.Parameters.AddWithValue("@Email", model.Email);
                int exists = (int)checkCmd.ExecuteScalar();

                if (exists > 0)
                {
                    ModelState.AddModelError("", "Email already exists.");
                    return View(model);
                }

                // Insert new user (plain text password)
                string insertUserQuery = @"
                    INSERT INTO Users (FullName, Email, PasswordHash, Phone, Role, Status)
                    VALUES (@Name, @Email, @Password, @Phone, @Role, 'Active');
                    SELECT SCOPE_IDENTITY();";

                SqlCommand insertUserCmd = new SqlCommand(insertUserQuery, con);
                insertUserCmd.Parameters.AddWithValue("@Name", model.FullName);
                insertUserCmd.Parameters.AddWithValue("@Email", model.Email);
                insertUserCmd.Parameters.AddWithValue("@Password", model.Password);
                insertUserCmd.Parameters.AddWithValue("@Phone", model.PhoneNumber);
                insertUserCmd.Parameters.AddWithValue("@Role", model.Role);

                int newUserId = Convert.ToInt32(insertUserCmd.ExecuteScalar());

                // If role is NOT Admin, create Employee record automatically
                if (model.Role != "Admin")
                {
                    string insertEmployeeQuery = @"
        INSERT INTO Employees (UserID, CNIC, HireDate, Salary, Designation, IsActive)
        VALUES (@UserId, @CNIC, @HireDate, @Salary, @Designation, 1)";

                    SqlCommand insertEmpCmd = new SqlCommand(insertEmployeeQuery, con);

                    insertEmpCmd.Parameters.AddWithValue("@UserId", newUserId);
                    insertEmpCmd.Parameters.AddWithValue("@CNIC", "TEMP-" + newUserId);
                    insertEmpCmd.Parameters.AddWithValue("@HireDate", DateTime.Now);
                    insertEmpCmd.Parameters.AddWithValue("@Salary", 0);
                    insertEmpCmd.Parameters.AddWithValue("@Designation", model.Role);

                    insertEmpCmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("Login");
        }

        // ================= FORGOT PASSWORD =================
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public IActionResult ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            string password = null;
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = "SELECT PasswordHash FROM Users WHERE Email=@Email AND Status='Active'";
                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@Email", model.Email);
                password = cmd.ExecuteScalar()?.ToString();
            }

            if (!string.IsNullOrEmpty(password))
            {
                TempData["Message"] = $"Your password is: {password}. Please keep it safe.";
            }
            else
            {
                TempData["Message"] = "Email not found or inactive.";
            }

            return RedirectToAction("ForgotPasswordConfirmation");
        }

        public IActionResult ForgotPasswordConfirmation()
        {
            ViewBag.Message = TempData["Message"] ?? "Check your email for password recovery.";
            return View();
        }

        // ================= RESET PASSWORD =================
        [HttpGet]
        public IActionResult ResetPassword()
        {
            return View();
        }

        [HttpPost]
        public IActionResult ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string updateQuery = "UPDATE Users SET PasswordHash=@NewPass WHERE Email=@Email";
                SqlCommand cmd = new SqlCommand(updateQuery, con);
                cmd.Parameters.AddWithValue("@NewPass", model.Password);
                cmd.Parameters.AddWithValue("@Email", model.Email);
                int rowsAffected = cmd.ExecuteNonQuery();

                if (rowsAffected > 0)
                    TempData["Message"] = "Password reset successful. Please login with your new password.";
                else
                    TempData["Message"] = "Email not found.";
            }

            return RedirectToAction("Login");
        }

        // ================= LOGOUT =================
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToAction("Login");
        }
    }
}