# --- ETAPA 1: BUILD ---
# Imagen que contiene todas las herramientas pesadas instaladas para poder compilar el código en C#
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Define una configuracion interna en la imagen para crear y entrar a una carpeta llamada /app
# Es decir, está creando una carpeta /app adentro del disco duro virtual de un mini-Linux.
WORKDIR /app
# Agarra el archivo físico .csproj del proyecto y lo guarda en la carpeta /app
COPY backend_CLARA.csproj ./
# Ejecuta este comando mientras se está construyendo la imagen
# Descarga todas tus librerías de internet y las guarda como una nueva capa sólida dentro de este molde temporal
RUN dotnet restore
# Copia el resto del código fuente (controladores, modelos, etc.) y lo sella en la siguiente capa de la imagen
COPY . .
# Ejecuta el compilador de C# internamente. 
# Transforma todo el código humano en archivos binarios (.dll) y guarda el resultado en una carpeta llamada /out dentro de esta imagen temporal.
RUN dotnet publish -c Release -o /out

# --- ETAPA 2: PRODUCCIÓN (Ahora con el Runtime 10.0) ---
# Inicia la construcción de una Imagen base completamente nueva, la versión ASP.NET
# Esta imagen es mucho más ligera, limpia y no tiene compilador
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Configura el directorio principal dentro de esta nueva imagen definitiva
WORKDIR /app
# Se graba en la configuración interna que este molde está diseñado para recibir tráfico por el puerto 5133
EXPOSE 5133
# Va a la imagen temporal que etiquetamos como build, saca únicamente los binarios ya terminados (.dll) de la carpeta /out, y los inyecta en nuestra imagen definitiva. 
# La imagen pesada inicial se desecha.
COPY --from=build /out .
# Serie de comandos para iniciar el contenedor
ENTRYPOINT ["dotnet", "backend_CLARA.dll"]
