# Build Stage
FROM node:18-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm install
COPY . .
RUN npm run build

# Production Stage
FROM nginx:stable-alpine AS production

# Nastaven� pracovn� slo�ky
WORKDIR /usr/share/nginx/html

# Smaz�n� defaultn�ho obsahu Nginxu (aby tam nebyl v�choz� index.html)
RUN rm -rf ./*

# Kop�rov�n� sestaven� React aplikace
COPY --from=build /app/dist ./

# Kop�rov�n� vlastn�ho nginx konfigur�ku (d�le�it�!)
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Otev�en� portu 80
EXPOSE 80

# Spu�t�n� Nginxu
CMD ["nginx", "-g", "daemon off;"]