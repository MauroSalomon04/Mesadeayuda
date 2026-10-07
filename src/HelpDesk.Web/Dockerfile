# Frontend: React + TypeScript compilado con esbuild y servido por nginx.
# nginx también reenvía /api/ al backend, así el navegador ve un único origen
# (necesario para la cookie de sesión SameSite=Strict y la política CSP 'self').

# ---------- compilación ----------
FROM node:22-alpine AS build
WORKDIR /web

COPY package.json package-lock.json* ./
RUN npm install --no-audit --no-fund

COPY . .
RUN SALIDA=/web/dist npm run build

# ---------- servidor web ----------
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY seguridad.inc /etc/nginx/conf.d/seguridad.inc
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 80
