const express = require("express");
const { Pool } = require("pg");
const cors = require("cors");
const jwt = require("jsonwebtoken");
require("dotenv").config();

const app = express();
app.use(cors());
app.use(express.json());

// DB connection - using AUTH_DATABASE_URL for clarity (this is the auth database)
const db = new Pool({ connectionString: process.env.AUTH_DATABASE_URL });

// JWT setup
const JWT_SECRET = process.env.JWT_SECRET;
if (!JWT_SECRET) {
  throw new Error("JWT_SECRET is not defined in environment variables");
}

function signToken(payload) {
  return jwt.sign(payload, JWT_SECRET, { expiresIn: "24h" });
}

// Routes
app.use("/auth", require("./routes/auth")(db, JWT_SECRET, signToken));

// Static WebGL build
const path = require("path");
app.use(express.static(path.join(__dirname, "public")));

// Start server
const port = process.env.PORT || 8080;
app.listen(port, "0.0.0.0", () =>
  console.log(`API live: http://0.0.0.0:${port}`)
);

// Clean shutdown for auth server
process.on('SIGTERM', () => {
  console.log('Auth server shutting down gracefully...');
  
  // Close database connections
  if (db) {
    db.end(() => {
      console.log('Database connection closed');
      process.exit(0);
    });
  } else {
    process.exit(0);
  }
});

process.on('SIGINT', () => {
  console.log('Auth server interrupted, shutting down gracefully...');
  
  // Close database connections  
  if (db) {
    db.end(() => {
      console.log('Database connection closed');
      process.exit(0);
    });
  } else {
    process.exit(0);
  }
});