FROM node:20-alpine AS build
WORKDIR /app

COPY web/package.json ./package.json
RUN npm install --no-audit --no-fund

COPY web/ .
RUN npm run build

FROM nginx:1.27-alpine
COPY web/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
