const rateLimit = require("express-rate-limit");

// IP-based registration limiter: 5 registrations per IP per hour
// Note: 'admin' username bypasses this (for dev/testing purposes)
const registrationLimiter = rateLimit({
  windowMs: 60 * 60 * 1000, // 1 hour
  max: 5, // 5 registrations per hour per IP
  handler: (req, res) => {
    res.status(429).json({
      error: "Too many registrations",
      message: "Too many registrations from this IP, please try again later"
    });
  },
  standardHeaders: true,
  legacyHeaders: false,
  skipSuccessfulRequests: false,
  // Skip rate limiting for admin username
  skip: async (req, res) => {
    // Allow unlimited registrations for admin (dev/testing)
    if (req.body.username === 'admin') {
      return true;
    }
    return false;
  },
  // Default keyGenerator handles IPv6 properly
});

// Basic rate limiter for login endpoint (to prevent brute force)
// Note: Admin accounts bypass this via skip function
const loginRateLimiter = rateLimit({
  windowMs: 15 * 60 * 1000, // 15 minutes
  max: 10, // 10 attempts per 15 minutes per IP
  handler: (req, res) => {
    res.status(429).json({
      error: "Too many login attempts",
      message: "Too many login attempts from this IP, please try again later"
    });
  },
  standardHeaders: true,
  legacyHeaders: false,
  skipSuccessfulRequests: false,
  // Skip rate limiting for admin accounts
  skip: async (req, res) => {
    // Check if username is 'admin' or if user is in adminwhitelist
    if (req.body.username === 'admin') {
      return true; // Skip rate limiting for admin account
    }
    return false;
  },
  // Default keyGenerator handles IPv6 properly
});

// Account-specific login attempt tracking with database
class LoginAttemptTracker {
  constructor(db) {
    this.db = db;
    this.MAX_ATTEMPTS = 3;
    this.LOCKOUT_DURATION = 2 * 60 * 1000; // 2 minutes in milliseconds
  }

  async trackFailedAttempt(username, ip) {
    try {
      // Record the failed attempt
      await this.db.query(
        `INSERT INTO login_attempts (username, ip_address, attempt_time, successful)
         VALUES ($1, $2, NOW(), false)`,
        [username, ip]
      );

      // Clean up old attempts (older than lockout duration)
      await this.db.query(
        `DELETE FROM login_attempts
         WHERE username = $1
         AND attempt_time < NOW() - INTERVAL '${this.LOCKOUT_DURATION / 1000} seconds'`,
        [username]
      );

      // Check if account should be locked
      const recentAttempts = await this.db.query(
        `SELECT COUNT(*) as failed_count
         FROM login_attempts
         WHERE username = $1
         AND successful = false
         AND attempt_time > NOW() - INTERVAL '${this.LOCKOUT_DURATION / 1000} seconds'`,
        [username]
      );

      const failedCount = parseInt(recentAttempts.rows[0].failed_count);

      if (failedCount >= this.MAX_ATTEMPTS) {
        // Calculate when the lockout expires
        const lockoutExpiry = new Date(Date.now() + this.LOCKOUT_DURATION);

        // Store lockout in database
        await this.db.query(
          `INSERT INTO account_lockouts (username, locked_until, reason)
           VALUES ($1, $2, 'Too many failed login attempts')
           ON CONFLICT (username)
           DO UPDATE SET locked_until = $2, reason = 'Too many failed login attempts'`,
          [username, lockoutExpiry]
        );

        return {
          locked: true,
          lockedUntil: lockoutExpiry,
          remainingAttempts: 0
        };
      }

      return {
        locked: false,
        remainingAttempts: this.MAX_ATTEMPTS - failedCount
      };
    } catch (error) {
      console.error("Error tracking failed login attempt:", error);
      // Don't block login on error, but log it
      return { locked: false, remainingAttempts: this.MAX_ATTEMPTS };
    }
  }

  async trackSuccessfulLogin(username, ip) {
    try {
      // Record successful login
      await this.db.query(
        `INSERT INTO login_attempts (username, ip_address, attempt_time, successful)
         VALUES ($1, $2, NOW(), true)`,
        [username, ip]
      );

      // Clear any lockout for this account
      await this.db.query(
        `DELETE FROM account_lockouts WHERE username = $1`,
        [username]
      );

      // Clean up old login attempts for this user
      await this.db.query(
        `DELETE FROM login_attempts
         WHERE username = $1
         AND attempt_time < NOW() - INTERVAL '24 hours'`,
        [username]
      );
    } catch (error) {
      console.error("Error tracking successful login:", error);
      // Don't block login on error
    }
  }

  async isAccountLocked(username) {
    try {
      const result = await this.db.query(
        `SELECT locked_until
         FROM account_lockouts
         WHERE username = $1 AND locked_until > NOW()`,
        [username]
      );

      if (result.rows.length > 0) {
        return {
          locked: true,
          lockedUntil: result.rows[0].locked_until
        };
      }

      return { locked: false };
    } catch (error) {
      console.error("Error checking account lock status:", error);
      // Don't block on error
      return { locked: false };
    }
  }

  // Middleware to check if account is locked before login attempt
  createAccountLockMiddleware() {
    return async (req, res, next) => {
      const { username } = req.body;

      if (!username) {
        return next();
      }

      const lockStatus = await this.isAccountLocked(username);

      if (lockStatus.locked) {
        const remainingTime = Math.ceil((new Date(lockStatus.lockedUntil) - new Date()) / 1000);
        return res.status(429).json({
          error: "Account temporarily locked",
          message: `Too many failed login attempts. Please try again in ${remainingTime} seconds.`,
          lockedUntil: lockStatus.lockedUntil
        });
      }

      next();
    };
  }
}

module.exports = {
  registrationLimiter,
  loginRateLimiter,
  LoginAttemptTracker
};