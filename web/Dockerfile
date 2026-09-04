FROM node:25-alpine@sha256:bdf2cca6fe3dabd014ea60163eca3f0f7015fbd5c7ee1b0e9ccb4ced6eb02ef4 AS build
WORKDIR /app

COPY web/package.json ./package.json
COPY web/package-lock.json ./package-lock.json
RUN npm ci --no-audit --no-fund

COPY web/ .
RUN npm run build

FROM nginx:1.31.5-alpine@sha256:72ba65eb42c10344912a84ff42408db7d34f2feb642204570ab8fc5ffd29f1d3
# Forces the apk upgrade below to re-run against current package indexes instead of reusing a
# stale cached layer, so newly published Alpine security patches actually land in the image.
ARG PACKAGE_CACHE_BUST=unset
RUN echo "package-cache-bust: ${PACKAGE_CACHE_BUST}" && apk del \
		nginx-module-acme \
		nginx-module-geoip \
		nginx-module-image-filter \
		nginx-module-njs \
		nginx-module-xslt \
		nginx \
	&& apk add --no-cache nginx \
	&& apk upgrade --no-cache \
	&& rm -f /etc/nginx/conf.d/default.conf \
	&& sed -i '/^user[[:space:]]/d' /etc/nginx/nginx.conf \
	&& sed -i -E '/^[[:space:]]*pid[[:space:]]/d' /etc/nginx/nginx.conf \
	&& sed -i '1ipid /tmp/nginx.pid;' /etc/nginx/nginx.conf \
	&& sed -i -E '/^[[:space:]]*ssl_(protocols|session_cache|session_timeout|session_tickets)[[:space:]]/d' /etc/nginx/nginx.conf \
	&& mkdir -p /usr/share/nginx/html \
	&& chown -R nginx:nginx /var/cache/nginx /var/run /etc/nginx /usr/share/nginx/html
COPY web/nginx/default.conf /etc/nginx/http.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

USER nginx
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=4 \
	CMD wget --quiet --output-document=/dev/null http://127.0.0.1:8080/health || exit 1

CMD ["nginx", "-g", "daemon off;"]
