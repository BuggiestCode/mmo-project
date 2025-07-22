# Use official Node.js base image
FROM node:20

# Set working directory
WORKDIR /app

# Copy package.json and install dependencies
COPY package*.json ./
RUN npm install --production

# Copy the rest of your app
COPY . .

# This must match Fly's expected port
EXPOSE 8080

# Start the server
CMD ["node", "server.js"]