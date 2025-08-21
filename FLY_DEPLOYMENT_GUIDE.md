# Fly.io Deployment Guide for MMO

## Architecture Overview

### Core Apps (Per Environment)
- **Auth/Frontend App**: Handles authentication and serves Unity WebGL build
- **Database Cluster**: PostgreSQL cluster with auth and game databases
- **World Servers**: Multiple game world instances (scalable)

### Environments
- **Staging**: For testing and development
- **Production**: Live environment

## Initial Setup Commands

### 1. Create Database Cluster (Staging)

```bash
# Create Postgres cluster
fly postgres create --name mmo-db-staging --region lhr --initial-cluster-size 1

# Note the connection details from output:
# Username: postgres
# Password: [GENERATED_PASSWORD]
# Hostname: mmo-db-staging.internal
# Proxy Port: 5432
# Postgres Port: 5433

# Connect to create databases
fly proxy 5455:5432 -a mmo-db-staging

# In another terminal, create databases
psql -h localhost -p 5455 -U postgres -d postgres

# Run these SQL commands:
CREATE DATABASE mmo_auth;
CREATE DATABASE mmo_game;
\q

# Import schemas
psql -h localhost -p 5455 -U postgres -d mmo_auth < mmo-db/auth_schema.sql
psql -h localhost -p 5455 -U postgres -d mmo_game < mmo-db/game_schema.sql
```

### 2. Create Auth/Frontend App (Staging)

```bash
cd mmo-backend

# Create the app
fly apps create mmo-auth-frontend-staging --region lhr

# Set secrets (JWT must be shared across all apps in the same environment)
fly secrets set \
  AUTH_DATABASE_URL=postgres://postgres:[PASSWORD]@mmo-db-staging.flycast:5432/mmo_auth \
  JWT_SECRET=[GENERATE_A_SECURE_SECRET] \
  -a mmo-auth-frontend-staging

# Deploy (Use tools in Unity but for the sake of keeping track)
fly deploy -a mmo-auth-frontend-staging
```

### 3. Create World Server (Staging)

```bash
# For each world (e.g., world1, world2, etc.)
fly apps create mmo-world1-staging --region lhr

# Set secrets for world1
fly secrets set \
  AUTH_DATABASE_URL=postgres://postgres:[PASSWORD]@mmo-db-staging.flycast:5432/mmo_auth \
  GAME_DATABASE_URL=postgres://postgres:[PASSWORD]@mmo-db-staging.flycast:5432/mmo_game \
  JWT_SECRET=[SAME_AS_AUTH_APP] \
  WORLD_NAME=world1-staging \
  -a mmo-world1-staging

# Deploy
fly deploy -a mmo-world1-staging
```

## Adding a New World Server

### Script: `create-world.sh`

```bash
#!/bin/bash

# Usage: ./create-world.sh <world-number> <environment>
# Example: ./create-world.sh 2 staging

WORLD_NUM=$1
ENV=$2

if [ "$ENV" = "staging" ]; then
    DB_HOST="mmo-db-staging"
    AUTH_FRONTEND="mmo-auth-frontend-staging"
    DB_PASSWORD="[STAGING_DB_PASSWORD]"
    JWT_SECRET="[STAGING_JWT_SECRET]"
elif [ "$ENV" = "prod" ]; then
    DB_HOST="mmo-db-prod"
    AUTH_FRONTEND="mmo-auth-frontend-prod"
    DB_PASSWORD="[PROD_DB_PASSWORD]"
    JWT_SECRET="[PROD_JWT_SECRET]"
else
    echo "Invalid environment. Use 'staging' or 'prod'"
    exit 1
fi

APP_NAME="mmo-world${WORLD_NUM}-${ENV}"
WORLD_NAME="world${WORLD_NUM}-${ENV}"

echo "Creating world server: $APP_NAME"

# Create app
fly apps create $APP_NAME --region lhr

# Set secrets
fly secrets set \
  AUTH_DATABASE_URL=postgres://postgres:${DB_PASSWORD}@${DB_HOST}.flycast:5432/mmo_auth \
  GAME_DATABASE_URL=postgres://postgres:${DB_PASSWORD}@${DB_HOST}.flycast:5432/mmo_game \
  JWT_SECRET=${JWT_SECRET} \
  WORLD_NAME=${WORLD_NAME} \
  -a $APP_NAME

# Deploy
fly deploy -a $APP_NAME

echo "World server $APP_NAME created and deployed!"
```

## Environment Variables Reference

### Auth/Frontend App
- `AUTH_DATABASE_URL`: Connection to auth database (users, sessions)
- `JWT_SECRET`: Secret for JWT token signing

### World Server
- `AUTH_DATABASE_URL`: Connection to auth database (for session management)
- `GAME_DATABASE_URL`: Connection to game database (players, game state)
- `JWT_SECRET`: Must match auth app's JWT_SECRET
- `WORLD_NAME`: Unique identifier for this world instance

## Database Schemas

### Auth Database (`mmo_auth`)
```sql
-- Users table
CREATE TABLE users (
  id SERIAL PRIMARY KEY,
  username VARCHAR(255) UNIQUE NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Active sessions table
CREATE TABLE active_sessions (
  user_id INTEGER PRIMARY KEY,
  world VARCHAR(255) NOT NULL,
  connection_state SMALLINT NOT NULL DEFAULT 0,
  last_heartbeat TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

### Game Database (`mmo_game`)
```sql
-- Players table
CREATE TABLE players (
  id SERIAL PRIMARY KEY,
  user_id INTEGER NOT NULL UNIQUE,
  x INTEGER NOT NULL DEFAULT 0,
  y INTEGER NOT NULL DEFAULT 0,
  facing SMALLINT DEFAULT 0
);
```

## Monitoring & Maintenance

### Check app status
```bash
fly status -a <app-name>
```

### View logs
```bash
fly logs -a <app-name>
```

### Scale instances
```bash
fly scale count 2 -a <app-name>
```

### Connect to database
```bash
fly proxy 5455:5432 -a mmo-db-staging
psql -h localhost -p 5455 -U postgres -d mmo_auth
```

## Production Setup

For production, repeat the same steps but with:
- App names: `mmo-auth-frontend-prod`, `mmo-db-prod`, `mmo-world1-prod`, etc.
- Different secrets (generate new secure values)
- Consider higher cluster sizes for database
- Enable auto-scaling for world servers

## Security Notes

1. Generate unique secrets for each environment
2. Never commit secrets to version control
3. Use Fly's secret management for all sensitive data
4. Regularly rotate JWT_SECRET
5. Use strong passwords for database

## Cleanup Commands

### Delete an app
```bash
fly apps destroy <app-name>
```

### Remove a secret
```bash
fly secrets unset <SECRET_NAME> -a <app-name>
```

## Troubleshooting

### Database connection issues
- Verify database name uses underscores (mmo_auth, not mmo-auth)
- Check that .flycast domain is used for internal connections
- Ensure secrets don't have quotes around values

### World server not connecting
- Verify JWT_SECRET matches between auth and world servers
- Check WORLD_NAME is unique across all servers
- Ensure both AUTH_DATABASE_URL and GAME_DATABASE_URL are set



### Delete entry from databases (list the user_id and find matching)
-- in mmo_game
BEGIN;
DELETE FROM players WHERE user_id = 24;
COMMIT;

-- in mmo_auth
BEGIN;
DELETE FROM users WHERE id = 24;
COMMIT;

### Add column to database (non destructively) via CLI (proxy tunnel tool)
ALTER TABLE players ADD COLUMN new_column_name SMALLINT DEFAULT 0;

### Modify data via CLI (proxy tunnel tool)
Update all existing rows:
UPDATE players SET column_name = new_value;

Update a specific entry by ID:
UPDATE players SET column_name = new_value WHERE id = 123;

Update based on other conditions:
UPDATE players SET column_name = new_value WHERE username = 'player_name';

Update multiple columns at once:
UPDATE players SET column_name = new_value, another_column = another_value WHERE id = 123;

Examples:
-- Set all players' eye_color_index to 1
UPDATE players SET eye_color_index = 1;

-- Set specific player's eye color
UPDATE players SET eye_color_index = 2 WHERE id = 5;

-- Update multiple appearance values for one player
UPDATE players SET eye_color_index = 2, hair_style_index = 3 WHERE username = 'john_doe';
