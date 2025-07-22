# Use official Node.js base image
FROM node:20

# Set working directory
WORKDIR /app

# Copy package.json and install dependencies
COPY package*.json ./
RUN npm install --production

# Copy the rest of your app
COPY . .

# Expose WebSocket port (matches fly.toml)
EXPOSE 8080

# Start the server
CMD ["node", "game_server.js"]