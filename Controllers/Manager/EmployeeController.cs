using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Employee;
using System;
using System.Collections.Generic;
using System.Linq;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class EmployeeController : Controller
    {
        private readonly Db _db;
        public EmployeeController(Db db) => _db = db;

        // ========== LIST EMPLOYEES ==========
        public IActionResult EmployeeList(string search = "", bool showInactive = false)
        {
            var employees = new List<EmployeeViewModel>();

            using var conn = _db.GetConnection();
            conn.Open();

            string query = @"
                SELECT e.EmployeeID, e.UserID, u.FullName, u.Email, u.Role, e.CNIC, u.Phone, 
                       e.HireDate, e.Salary, e.Designation, u.Status
                FROM Employees e
                JOIN Users u ON e.UserID = u.UserID
                WHERE 1=1";

            if (!showInactive)
                query += " AND u.Status = 'Active'";

            if (!string.IsNullOrEmpty(search))
                query += " AND (u.FullName LIKE @search OR u.Email LIKE @search OR e.CNIC LIKE @search)";

            using var cmd = new SqlCommand(query, conn);
            if (!string.IsNullOrEmpty(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                employees.Add(new EmployeeViewModel
                {
                    EmployeeID = reader.GetInt32(0),
                    UserID = reader.GetInt32(1),
                    FullName = reader.GetString(2),
                    Email = reader.GetString(3),
                    Role = reader.GetString(4),
                    CNIC = reader.GetString(5),
                    Phone = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    HireDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                    Salary = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                    Designation = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    IsActive = reader.GetString(10) == "Active"
                });
            }

            ViewBag.ShowInactive = showInactive;
            ViewBag.Search = search;
            return View(employees);
        }

        // ========== ADD EMPLOYEE ==========
        [HttpGet]
        public IActionResult AddEmployee() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddEmployee(EmployeeViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var conn = _db.GetConnection();

            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                // ================= DUPLICATE EMAIL CHECK =================

                string checkEmailQuery =
                    "SELECT COUNT(*) FROM Users WHERE Email = @Email";

                using var cmdCheckEmail =
                    new SqlCommand(checkEmailQuery, conn, transaction);

                cmdCheckEmail.Parameters.AddWithValue("@Email", model.Email);

                int emailExists =
                    Convert.ToInt32(cmdCheckEmail.ExecuteScalar());

                if (emailExists > 0)
                {
                    TempData["Error"] = "Email already exists.";

                    return View(model);
                }

                // ================= DUPLICATE CNIC CHECK =================

                string checkCnicQuery =
                    "SELECT COUNT(*) FROM Employees WHERE CNIC = @CNIC";

                using var cmdCheckCnic =
                    new SqlCommand(checkCnicQuery, conn, transaction);

                cmdCheckCnic.Parameters.AddWithValue("@CNIC", model.CNIC);

                int cnicExists =
                    Convert.ToInt32(cmdCheckCnic.ExecuteScalar());

                if (cnicExists > 0)
                {
                    TempData["Error"] = "CNIC already exists.";

                    return View(model);
                }

                // ================= SECURITY CHECK =================

                if (User.IsInRole("Manager") && model.Role == "Admin")
                {
                    TempData["Error"] =
                        "Managers cannot create Admin accounts.";

                    return View(model);
                }

                // ================= INSERT USER =================

                string userQuery = @"
            INSERT INTO Users
            (
                Role,
                FullName,
                Email,
                PasswordHash,
                Phone,
                Status,
                CreatedAt
            )
            VALUES
            (
                @Role,
                @FullName,
                @Email,
                'default123',
                @Phone,
                'Active',
                GETDATE()
            );

            SELECT SCOPE_IDENTITY();";

                using var cmdUser =
                    new SqlCommand(userQuery, conn, transaction);

                cmdUser.Parameters.AddWithValue("@Role", model.Role);

                cmdUser.Parameters.AddWithValue("@FullName", model.FullName);

                cmdUser.Parameters.AddWithValue("@Email", model.Email);

                cmdUser.Parameters.AddWithValue("@Phone",
                    string.IsNullOrEmpty(model.Phone)
                    ? (object)DBNull.Value
                    : model.Phone);

                int userId =
                    Convert.ToInt32(cmdUser.ExecuteScalar());

                // ================= INSERT EMPLOYEE =================

                string empQuery = @"
            INSERT INTO Employees
            (
                UserID,
                CNIC,
                HireDate,
                Salary,
                Designation
            )
            VALUES
            (
                @UserId,
                @CNIC,
                @HireDate,
                @Salary,
                @Designation
            )";

                using var cmdEmp =
                    new SqlCommand(empQuery, conn, transaction);

                cmdEmp.Parameters.AddWithValue("@UserId", userId);

                cmdEmp.Parameters.AddWithValue("@CNIC", model.CNIC);

                cmdEmp.Parameters.AddWithValue("@HireDate",
                    model.HireDate ?? (object)DBNull.Value);

                cmdEmp.Parameters.AddWithValue("@Salary", model.Salary);

                cmdEmp.Parameters.AddWithValue("@Designation",
                    string.IsNullOrEmpty(model.Designation)
                    ? (object)DBNull.Value
                    : model.Designation);

                cmdEmp.ExecuteNonQuery();

                transaction.Commit();

                TempData["Success"] =
                    "Employee added successfully.";

                return RedirectToAction("EmployeeList");
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                TempData["Error"] =
                    "Error adding employee: " + ex.Message;

                return View(model);
            }
        }

        // ========== EDIT EMPLOYEE ==========
        [HttpGet]
        public IActionResult EditEmployee(int id)
        {
            var employee = GetEmployeeById(id);
            if (employee == null) return NotFound();
            return View(employee);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditEmployee(EmployeeViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var conn = _db.GetConnection();

            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                // ================= SECURITY CHECK =================

                if (User.IsInRole("Manager") && model.Role == "Admin")
                {
                    TempData["Error"] =
                        "Managers cannot assign Admin role.";

                    return View(model);
                }

                // ================= DUPLICATE EMAIL CHECK =================

                string checkEmail = @"
            SELECT COUNT(*)
            FROM Users
            WHERE Email = @Email
            AND UserID != @UserID";

                using var cmdCheckEmail =
                    new SqlCommand(checkEmail, conn, transaction);

                cmdCheckEmail.Parameters.AddWithValue("@Email", model.Email);

                cmdCheckEmail.Parameters.AddWithValue("@UserID", model.UserID);

                int emailExists =
                    Convert.ToInt32(cmdCheckEmail.ExecuteScalar());

                if (emailExists > 0)
                {
                    TempData["Error"] = "Email already exists.";

                    return View(model);
                }

                // ================= DUPLICATE CNIC CHECK =================

                string checkCnic = @"
            SELECT COUNT(*)
            FROM Employees
            WHERE CNIC = @CNIC
            AND EmployeeID != @EmployeeID";

                using var cmdCheckCnic =
                    new SqlCommand(checkCnic, conn, transaction);

                cmdCheckCnic.Parameters.AddWithValue("@CNIC", model.CNIC);

                cmdCheckCnic.Parameters.AddWithValue("@EmployeeID", model.EmployeeID);

                int cnicExists =
                    Convert.ToInt32(cmdCheckCnic.ExecuteScalar());

                if (cnicExists > 0)
                {
                    TempData["Error"] = "CNIC already exists.";

                    return View(model);
                }

                // ================= UPDATE USERS =================

                string userQuery = @"
            UPDATE Users
            SET
                FullName = @FullName,
                Email = @Email,
                Role = @Role,
                Phone = @Phone
            WHERE UserID = @UserID";

                using var cmdUser =
                    new SqlCommand(userQuery, conn, transaction);

                cmdUser.Parameters.AddWithValue("@FullName", model.FullName);

                cmdUser.Parameters.AddWithValue("@Email", model.Email);

                cmdUser.Parameters.AddWithValue("@Role", model.Role);

                cmdUser.Parameters.AddWithValue("@Phone",
                    string.IsNullOrEmpty(model.Phone)
                    ? (object)DBNull.Value
                    : model.Phone);

                cmdUser.Parameters.AddWithValue("@UserID", model.UserID);

                cmdUser.ExecuteNonQuery();

                // ================= UPDATE EMPLOYEE =================

                string empQuery = @"
            UPDATE Employees
            SET
                CNIC = @CNIC,
                HireDate = @HireDate,
                Salary = @Salary,
                Designation = @Designation
            WHERE EmployeeID = @EmployeeID";

                using var cmdEmp =
                    new SqlCommand(empQuery, conn, transaction);

                cmdEmp.Parameters.AddWithValue("@CNIC", model.CNIC);

                cmdEmp.Parameters.AddWithValue("@HireDate",
                    model.HireDate ?? (object)DBNull.Value);

                cmdEmp.Parameters.AddWithValue("@Salary", model.Salary);

                cmdEmp.Parameters.AddWithValue("@Designation",
                    string.IsNullOrEmpty(model.Designation)
                    ? (object)DBNull.Value
                    : model.Designation);

                cmdEmp.Parameters.AddWithValue("@EmployeeID", model.EmployeeID);

                cmdEmp.ExecuteNonQuery();

                transaction.Commit();

                TempData["Success"] = "Employee updated successfully.";

                return RedirectToAction("EmployeeList");
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                TempData["Error"] =
                    "Error updating employee: " + ex.Message;

                return View(model);
            }
        }

        // ========== DEACTIVATE EMPLOYEE ==========
        [HttpPost]
        public IActionResult DeactivateEmployee(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            string role = GetUserRoleByEmployee(id);

            if (string.IsNullOrEmpty(role))
                return Json(new { success = false, message = "Employee not found" });

            if (User.IsInRole("Manager") && role != "Cashier")
                return Json(new { success = false, message = "Not allowed" });

            string getUserId = "SELECT UserID FROM Employees WHERE EmployeeID = @id";
            using var cmdGet = new SqlCommand(getUserId, conn);
            cmdGet.Parameters.AddWithValue("@id", id);

            var result = cmdGet.ExecuteScalar();
            if (result == null)
                return Json(new { success = false, message = "Employee not found" });

            int userId = Convert.ToInt32(result);

            string query = "UPDATE Users SET Status = 'Inactive' WHERE UserID = @uid";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.ExecuteNonQuery();

            return Json(new { success = true });
        }
        [HttpPost]
        public IActionResult ReactivateEmployee(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            string role = GetUserRoleByEmployee(id);

            if (string.IsNullOrEmpty(role))
                return Json(new { success = false, message = "Employee not found" });

            if (User.IsInRole("Manager") && role != "Cashier")
                return Json(new { success = false, message = "Not allowed" });

            string getUserId = "SELECT UserID FROM Employees WHERE EmployeeID = @id";
            using var cmdGet = new SqlCommand(getUserId, conn);
            cmdGet.Parameters.AddWithValue("@id", id);

            var result = cmdGet.ExecuteScalar();
            if (result == null)
                return Json(new { success = false, message = "Employee not found." });

            int userId = Convert.ToInt32(result);

            string query = "UPDATE Users SET Status = 'Active' WHERE UserID = @uid";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.ExecuteNonQuery();

            return Json(new { success = true });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteEmployee(int id)
        {
            if (User.IsInRole("Manager"))
            {
                var role = GetUserRoleByEmployee(id);
                if (role == "Admin" || role == "Manager")
                    return Json(new { success = false, message = "Not allowed" });
            }
            using var conn = _db.GetConnection();

            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                string getUserId =
                    "SELECT UserID FROM Employees WHERE EmployeeID = @id";

                using var cmdGet =
                    new SqlCommand(getUserId, conn, transaction);

                cmdGet.Parameters.AddWithValue("@id", id);

                object result = cmdGet.ExecuteScalar();

                if (result == null)
                    return Json(new { success = false, message = "Employee not found." });

                int userId = Convert.ToInt32(result);

                // Delete child records first

                string deleteAttendance =
                    "DELETE FROM Attendance WHERE EmployeeID = @id";

                new SqlCommand(deleteAttendance, conn, transaction)
                {
                    Parameters =
            {
                new SqlParameter("@id", id)
            }
                }.ExecuteNonQuery();

                string deletePayroll =
                    "DELETE FROM Payroll WHERE EmployeeID = @id";

                new SqlCommand(deletePayroll, conn, transaction)
                {
                    Parameters =
            {
                new SqlParameter("@id", id)
            }
                }.ExecuteNonQuery();

                string deletePerformance =
                    "DELETE FROM Performance WHERE EmployeeID = @id";

                new SqlCommand(deletePerformance, conn, transaction)
                {
                    Parameters =
            {
                new SqlParameter("@id", id)
            }
                }.ExecuteNonQuery();

                string deleteEmployee =
                    "DELETE FROM Employees WHERE EmployeeID = @id";

                new SqlCommand(deleteEmployee, conn, transaction)
                {
                    Parameters =
            {
                new SqlParameter("@id", id)
            }
                }.ExecuteNonQuery();

                string deleteUser =
                    "DELETE FROM Users WHERE UserID = @uid";

                new SqlCommand(deleteUser, conn, transaction)
                {
                    Parameters =
            {
                new SqlParameter("@uid", userId)
            }
                }.ExecuteNonQuery();

                transaction.Commit();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
        // ========== ATTENDANCE ==========
        public IActionResult AttendanceReport(int? year, int? month)
        {
            int displayYear = year ?? DateTime.Now.Year;
            int displayMonth = month ?? DateTime.Now.Month;

            var employees = new List<EmployeeViewModel>();

            using var conn = _db.GetConnection();
            conn.Open();

            // ================= GET ACTIVE EMPLOYEES =================
            string empQuery = @"
        SELECT 
            e.EmployeeID,
            u.FullName
        FROM Employees e
        INNER JOIN Users u
            ON e.UserID = u.UserID
        WHERE u.Status = 'Active'
        ORDER BY u.FullName";

            using var cmdEmp = new SqlCommand(empQuery, conn);

            using var readerEmp = cmdEmp.ExecuteReader();

            while (readerEmp.Read())
            {
                employees.Add(new EmployeeViewModel
                {
                    EmployeeID = readerEmp.GetInt32(0),
                    FullName = readerEmp.GetString(1)
                });
            }

            readerEmp.Close();

            // ================= GET ATTENDANCE DATA =================
            var attendanceData =
                new Dictionary<int, Dictionary<DateTime, string>>();

            string attQuery = @"
        SELECT
            EmployeeID,
            AttendanceDate,
            Status
        FROM Attendance
        WHERE YEAR(AttendanceDate) = @year
          AND MONTH(AttendanceDate) = @month";

            using var cmdAtt = new SqlCommand(attQuery, conn);

            cmdAtt.Parameters.AddWithValue("@year", displayYear);
            cmdAtt.Parameters.AddWithValue("@month", displayMonth);

            using var readerAtt = cmdAtt.ExecuteReader();

            while (readerAtt.Read())
            {
                int empId = readerAtt.GetInt32(0);

                DateTime date =
                    readerAtt.GetDateTime(1).Date;

                string status =
                    readerAtt.IsDBNull(2)
                    ? ""
                    : readerAtt.GetString(2);

                if (!attendanceData.ContainsKey(empId))
                {
                    attendanceData[empId] =
                        new Dictionary<DateTime, string>();
                }

                attendanceData[empId][date] = status;
            }

            readerAtt.Close();

            // ================= CALENDAR DATES =================
            int daysInMonth =
                DateTime.DaysInMonth(displayYear, displayMonth);

            var dates =
                Enumerable.Range(1, daysInMonth)
                .Select(day =>
                    new DateTime(displayYear, displayMonth, day))
                .ToList();

            // ================= VIEWBAGS =================
            ViewBag.Dates = dates;

            ViewBag.Year = displayYear;

            ViewBag.Month = displayMonth;

            ViewBag.AttendanceData = attendanceData;

            ViewBag.PrevMonth =
                new DateTime(displayYear, displayMonth, 1)
                .AddMonths(-1);

            ViewBag.NextMonth =
                new DateTime(displayYear, displayMonth, 1)
                .AddMonths(1);

            return View(employees);
        }

        // ========== MANAGE ATTENDANCE ==========
        public IActionResult ManageAttendance(DateTime? date)
        {
            DateTime selectedDate =
                (date ?? DateTime.Today).Date;

            var employees =
                new List<ManageAttendanceViewModel>();

            using var conn = _db.GetConnection();

            conn.Open();

            string query = @"
        SELECT
            e.EmployeeID,
            u.FullName,
            a.AttendanceID,
            a.CheckInTime,
            a.CheckOutTime,
            ISNULL(a.Status, 'Absent')
        FROM Employees e
        INNER JOIN Users u
            ON e.UserID = u.UserID
        LEFT JOIN Attendance a
            ON e.EmployeeID = a.EmployeeID
            AND CAST(a.AttendanceDate AS DATE) = @date
        WHERE u.Status = 'Active'
        ORDER BY u.FullName";

            using var cmd = new SqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@date", selectedDate);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                employees.Add(new ManageAttendanceViewModel
                {
                    EmployeeID = reader.GetInt32(0),

                    FullName = reader.GetString(1),

                    AttendanceID = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),

                    CheckInTime =
                        reader.IsDBNull(3)
                        ? null
                        : reader.GetTimeSpan(3),

                    CheckOutTime =
                        reader.IsDBNull(4)
                        ? null
                        : reader.GetTimeSpan(4),

                    Status = reader.GetString(5)
                });
            }

            ViewBag.SelectedDate = selectedDate;

            return View(employees);
        }

        // ========== MARK ATTENDANCE MANUALLY ==========
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkAttendance(int employeeId, string actionType)
        {
            if (employeeId <= 0)
                return Json(new { success = false, message = "Invalid Employee Selection." });

            DateTime currentDateTime = DateTime.Now;
            DateTime currentDate = currentDateTime.Date;
            TimeSpan currentTime = currentDateTime.TimeOfDay;

            try
            {
                using var conn = _db.GetConnection();
                conn.Open();

                // Check for existing record matching the unique key constraint (EmployeeID, AttendanceDate)
                string checkQuery = "SELECT COUNT(1) FROM Attendance WHERE EmployeeID = @eid AND AttendanceDate = @date";
                using var checkCmd = new SqlCommand(checkQuery, conn);
                checkCmd.Parameters.AddWithValue("@eid", employeeId);
                checkCmd.Parameters.AddWithValue("@date", currentDate);
                int recordExists = Convert.ToInt32(checkCmd.ExecuteScalar());

                if (actionType.Equals("CheckIn", StringComparison.OrdinalIgnoreCase))
                {
                    if (recordExists > 0)
                    {
                        // Record already initialized for today, perform safe update on CheckInTime
                        string updateQuery = "UPDATE Attendance SET CheckInTime = @time, Status = 'Present' WHERE EmployeeID = @eid AND AttendanceDate = @date";
                        using var updateCmd = new SqlCommand(updateQuery, conn);
                        updateCmd.Parameters.AddWithValue("@time", currentTime);
                        updateCmd.Parameters.AddWithValue("@eid", employeeId);
                        updateCmd.Parameters.AddWithValue("@date", currentDate);
                        updateCmd.ExecuteNonQuery();
                    }
                    else
                    {
                        // Clean insert
                        string insertQuery = "INSERT INTO Attendance (EmployeeID, AttendanceDate, CheckInTime, Status) VALUES (@eid, @date, @time, 'Present')";
                        using var insertCmd = new SqlCommand(insertQuery, conn);
                        insertCmd.Parameters.AddWithValue("@eid", employeeId);
                        insertCmd.Parameters.AddWithValue("@date", currentDate);
                        insertCmd.Parameters.AddWithValue("@time", currentTime);
                        insertCmd.ExecuteNonQuery();
                    }
                }
                else if (actionType.Equals("CheckOut", StringComparison.OrdinalIgnoreCase))
                {
                    if (recordExists > 0)
                    {
                        // Calculate overtime hours dynamically if CheckIn exists
                        string getCheckInQuery = "SELECT CheckInTime FROM Attendance WHERE EmployeeID = @eid AND AttendanceDate = @date";
                        using var getCmd = new SqlCommand(getCheckInQuery, conn);
                        getCmd.Parameters.AddWithValue("@eid", employeeId);
                        getCmd.Parameters.AddWithValue("@date", currentDate);
                        var checkInObj = getCmd.ExecuteScalar();

                        decimal overtime = 0;
                        if (checkInObj != DBNull.Value && checkInObj != null)
                        {
                            TimeSpan checkInTime = (TimeSpan)checkInObj;
                            double workedHours = (currentTime - checkInTime).TotalHours;
                            if (workedHours > 8.0)
                            {
                                overtime = (decimal)(workedHours - 8.0);
                            }
                        }

                        string updateQuery = @"UPDATE Attendance 
                                               SET CheckOutTime = @time, OvertimeHours = @overtime 
                                               WHERE EmployeeID = @eid AND AttendanceDate = @date";
                        using var updateCmd = new SqlCommand(updateQuery, conn);
                        updateCmd.Parameters.AddWithValue("@time", currentTime);
                        updateCmd.Parameters.AddWithValue("@overtime", overtime);
                        updateCmd.Parameters.AddWithValue("@eid", employeeId);
                        updateCmd.Parameters.AddWithValue("@date", currentDate);
                        updateCmd.ExecuteNonQuery();
                    }
                    else
                    {
                        // No Check-In record exists for today yet, handle gracefully
                        string insertQuery = "INSERT INTO Attendance (EmployeeID, AttendanceDate, CheckOutTime, Status) VALUES (@eid, @date, @time, 'Present')";
                        using var insertCmd = new SqlCommand(insertQuery, conn);
                        insertCmd.Parameters.AddWithValue("@eid", employeeId);
                        insertCmd.Parameters.AddWithValue("@date", currentDate);
                        insertCmd.Parameters.AddWithValue("@time", currentTime);
                        insertCmd.ExecuteNonQuery();
                    }
                }

                return Json(new { success = true, message = "Attendance logged successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Database Error: " + ex.Message });
            }
        }
        [HttpPost]
        public IActionResult DeleteAttendance(int id)
        {
            using var conn = _db.GetConnection();

            conn.Open();

            string query =
                "DELETE FROM Attendance WHERE AttendanceID = @id";

            using var cmd =
                new SqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@id", id);

            cmd.ExecuteNonQuery();

            return Json(new { success = true });
        }
        // ========== PAYROLL ==========
        public IActionResult Payroll(string month)
        {
            if (string.IsNullOrEmpty(month))
                month = DateTime.Now.ToString("yyyy-MM");
            var payrolls = new List<PayrollViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.PayrollID, p.EmployeeID, u.FullName, p.Month, p.BasicSalary, p.Bonus, p.Deductions, p.NetSalary, p.PaymentDate
                FROM Payroll p
                JOIN Employees e ON p.EmployeeID = e.EmployeeID
                JOIN Users u ON e.UserID = u.UserID
                WHERE p.Month = @month
                ORDER BY u.FullName";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@month", month);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                payrolls.Add(new PayrollViewModel
                {
                    PayrollID = reader.GetInt32(0),
                    EmployeeID = reader.GetInt32(1),
                    EmployeeName = reader.GetString(2),
                    Month = reader.GetString(3),
                    BasicSalary = reader.GetDecimal(4),
                    Bonus = reader.GetDecimal(5),
                    Deductions = reader.GetDecimal(6),
                    NetSalary = reader.GetDecimal(7),
                    PaymentDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
                });
            }
            ViewBag.Month = month;
            return View(payrolls);
        }

        [HttpPost]
        public IActionResult GeneratePayroll(string month)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            // Get all active employees
            string getEmployees = @"
                SELECT e.EmployeeID, e.Salary, u.FullName
                FROM Employees e
                JOIN Users u ON e.UserID = u.UserID
                WHERE u.Status = 'Active'";
            var employees = new List<(int Id, decimal Salary, string Name)>();
            using var cmdGet = new SqlCommand(getEmployees, conn);
            using var reader = cmdGet.ExecuteReader();
            while (reader.Read())
                employees.Add((reader.GetInt32(0), reader.GetDecimal(1), reader.GetString(2)));
            reader.Close();

            foreach (var emp in employees)
            {
                // Check if already generated
                string check = "SELECT COUNT(*) FROM Payroll WHERE EmployeeID = @eid AND Month = @month";
                using var cmdCheck = new SqlCommand(check, conn);
                cmdCheck.Parameters.AddWithValue("@eid", emp.Id);
                cmdCheck.Parameters.AddWithValue("@month", month);
                int exists = (int)cmdCheck.ExecuteScalar();
                if (exists > 0) continue;

                // ===== ATTENDANCE BONUS/DEDUCTION =====

                int presentDays = 0;
                decimal overtimeHours = 0;

                string attendanceQuery = @"
SELECT
    COUNT(CASE WHEN Status = 'Present' THEN 1 END),
    ISNULL(SUM(OvertimeHours),0)
FROM Attendance
WHERE EmployeeID = @eid
AND FORMAT(AttendanceDate, 'yyyy-MM') = @month";

                using var cmdAttendance = new SqlCommand(attendanceQuery, conn);
                cmdAttendance.Parameters.AddWithValue("@eid", emp.Id);
                cmdAttendance.Parameters.AddWithValue("@month", month);

                using var readerAtt = cmdAttendance.ExecuteReader();
                readerAtt.Read();

                presentDays = readerAtt.GetInt32(0);
                overtimeHours = readerAtt.GetDecimal(1);
                readerAtt.Close();

                // ===== CALCULATIONS =====

                decimal bonus = overtimeHours * 200;

                decimal deductions = presentDays < 20
                    ? (20 - presentDays) * 500
                    : 0;

                decimal overtimeAmount = overtimeHours * 200;

                decimal net =
                    emp.Salary +
                    bonus +
                    overtimeAmount -
                    deductions;

                string insert = @"
                    INSERT INTO Payroll(EmployeeID,Month,BasicSalary,Bonus,Deductions,OvertimeAmount,NetSalary,PaymentDate)
                    VALUES(@eid,@month,@basic,@bonus,@ded,@overtime,@net,NULL);";
                using var cmdIns = new SqlCommand(insert, conn);
                cmdIns.Parameters.AddWithValue("@eid", emp.Id);
                cmdIns.Parameters.AddWithValue("@month", month);
                cmdIns.Parameters.AddWithValue("@basic", emp.Salary);
                cmdIns.Parameters.AddWithValue("@bonus", bonus);
                cmdIns.Parameters.AddWithValue("@ded", deductions);
                cmdIns.Parameters.AddWithValue("@net", net);
                cmdIns.Parameters.AddWithValue("@overtime", overtimeAmount);
                cmdIns.ExecuteNonQuery();
            }
            TempData["Success"] = $"Payroll generated for {month}";
            return RedirectToAction("Payroll", new { month });
        }

        [HttpPost]
        public IActionResult MarkPayrollPaid(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "UPDATE Payroll SET PaymentDate = GETDATE() WHERE PayrollID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return Json(new { success = true });
        }

        // ========== PERFORMANCE ==========
        public IActionResult Performance(int? employeeId)
        {
            var reviews = new List<PerformanceViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.PerformanceID, p.EmployeeID, u.FullName, p.ReviewDate, p.SalesTarget, p.SalesAchieved, p.CustomerRating, p.Comments
                FROM Performance p
                JOIN Employees e ON p.EmployeeID = e.EmployeeID
                JOIN Users u ON e.UserID = u.UserID
                WHERE (@empId IS NULL OR p.EmployeeID = @empId)
                ORDER BY p.ReviewDate DESC";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@empId",employeeId.HasValue ? employeeId.Value : DBNull.Value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                reviews.Add(new PerformanceViewModel
                {
                    PerformanceID = reader.GetInt32(0),
                    EmployeeID = reader.GetInt32(1),
                    EmployeeName = reader.GetString(2),
                    ReviewDate = reader.GetDateTime(3),
                    SalesTarget = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                    SalesAchieved = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    CustomerRating = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    Comments = reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
            }
            ViewBag.Employees = GetActiveEmployees();
            return View(reviews);
        }

        [HttpPost]
        public IActionResult AddPerformance(PerformanceViewModel model)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                INSERT INTO Performance (EmployeeID, ReviewDate, SalesTarget, SalesAchieved, CustomerRating, Comments)
                VALUES (@eid, @date, @target, @achieved, @rating, @comments)";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@eid", model.EmployeeID);
            cmd.Parameters.AddWithValue("@date", model.ReviewDate);
            cmd.Parameters.AddWithValue("@target", model.SalesTarget ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@achieved", model.SalesAchieved ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@rating", model.CustomerRating ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@comments", model.Comments ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
            TempData["Success"] = "Performance review added.";
            return RedirectToAction("Performance");
        }

        // ========== expense ==========
        public IActionResult Expenses()
        {
            return View();
        }

        [HttpPost]
        public IActionResult AddExpense(
            int employeeId,
            string expenseType,
            decimal amount,
            string notes)
        {
            using var conn = _db.GetConnection();

            conn.Open();

            string query = @"
        INSERT INTO EmployeeExpenses
        (
            EmployeeID,
            ExpenseType,
            Amount,
            ExpenseDate,
            Notes
        )
        VALUES
        (
            @eid,
            @type,
            @amount,
            GETDATE(),
            @notes
        )";

            using var cmd =
                new SqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@eid", employeeId);

            cmd.Parameters.AddWithValue("@type", expenseType);

            cmd.Parameters.AddWithValue("@amount", amount);

            cmd.Parameters.AddWithValue("@notes",
                string.IsNullOrEmpty(notes)
                ? (object)DBNull.Value
                : notes);

            cmd.ExecuteNonQuery();

            TempData["Success"] = "Expense added.";

            return RedirectToAction("Expenses");
        }
        // ========== HELPERS ==========
        private EmployeeViewModel GetEmployeeById(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT e.EmployeeID, e.UserID, u.FullName, u.Email, u.Role, e.CNIC, u.Phone, 
                       e.HireDate, e.Salary, e.Designation, u.Status
                FROM Employees e
                JOIN Users u ON e.UserID = u.UserID
                WHERE e.EmployeeID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new EmployeeViewModel
                {
                    EmployeeID = reader.GetInt32(0),
                    UserID = reader.GetInt32(1),
                    FullName = reader.GetString(2),
                    Email = reader.GetString(3),
                    Role = reader.GetString(4),
                    CNIC = reader.GetString(5),
                    Phone = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    HireDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    Salary = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                    Designation = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    IsActive = reader.GetString(10) == "Active"
                };
            }
            return null;
        }

        private List<dynamic> GetActiveEmployees()
        {
            var list = new List<dynamic>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT e.EmployeeID, u.FullName
                FROM Employees e
                JOIN Users u ON e.UserID = u.UserID
                WHERE u.Status = 'Active'
                ORDER BY u.FullName";
            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(new { Value = reader.GetInt32(0), Text = reader.GetString(1) });
            return list;
        }
        private string GetUserRoleByEmployee(int employeeId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            string query = @"
                SELECT u.Role
                FROM Employees e
                JOIN Users u ON e.UserID = u.UserID
                WHERE e.EmployeeID = @id";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", employeeId);
            var result = cmd.ExecuteScalar();
            return result != null ? result.ToString() : "";
        }
    }
}