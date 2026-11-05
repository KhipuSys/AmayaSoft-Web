# AMAYASOFT — versión de trabajo

Esta carpeta contiene una copia editable del código fuente de MiniMarket Web.
Puedes realizar aquí tus cambios locales y, cuando estén listos, publicar el
proyecto para el hosting.

## Requisitos

- .NET SDK 9.0
- SQL Server accesible desde el equipo

## Abrir y ejecutar localmente

1. Abre `MiniMarket.Web.csproj` en Visual Studio o abre una terminal en esta
   carpeta.
2. Ajusta `ConnectionStrings:DefaultConnection` en `appsettings.json` para que
   apunte a tu instancia de SQL Server.
3. Ejecuta:

    ```powershell
    dotnet restore MiniMarket.Web.csproj
    dotnet run --project MiniMarket.Web.csproj
    ```

En una base vacía de desarrollo, el semillero requiere configurar antes la
variable de entorno `SeedData__InitialAdminPassword` con una contraseña que
cumpla la política de Identity. No hay una contraseña predeterminada en el
código; esta variable no se debe configurar en Production.

La configuración actual de `appsettings.json` apunta a `localhost\SQLEXPRESS`.
No contiene credenciales de hosting.

## Publicar después de tus modificaciones

Cuando el proyecto esté listo para desplegar, genera una publicación Release:

```powershell
dotnet publish MiniMarket.Web.csproj -c Release -o .\publicacion
```

Antes de desplegar, configura la cadena de conexión de producción mediante la
`ConnectionStrings__DefaultConnection`. Configura de la misma forma cualquier
token de servicios externos; no guardes credenciales en el código fuente.

## Facturación electrónica

Las ventas se registran primero con estado `Pendiente`. El sistema no contacta
Factiliza durante el cobro. Solo un usuario con rol `Administrador` puede abrir
el detalle y confirmar posteriormente la emisión.

La integración de producción está desactivada por defecto. Las series y el RUC
emisor se configuran como variables de entorno y no se guardan en el repositorio.
Antes de activar la emisión, Factiliza o el contador debe confirmar las series y
el correlativo correspondiente al RUC.

Configura estos secretos y controles en las variables de entorno del hosting:

```text
ASPNETCORE_ENVIRONMENT=Production
FactilizaSettings__FacturacionHabilitada=true
FactilizaSettings__Produccion__SeriesConfirmadas=true
FactilizaSettings__Produccion__EmpresaRuc=<RUC EMISOR>
FactilizaSettings__Produccion__BoletaSerie=<SERIE BOLETA CONFIRMADA>
FactilizaSettings__Produccion__FacturaSerie=<SERIE FACTURA CONFIRMADA>
FactilizaSettings__Produccion__BoletaCorrelativoInicial=<SIGUIENTE CORRELATIVO BOLETA>
FactilizaSettings__Produccion__FacturaCorrelativoInicial=<SIGUIENTE CORRELATIVO FACTURA>
FactilizaSettings__Produccion__Token=<TOKEN DE PRODUCCION>
FactilizaSettings__ConsultaToken=<TOKEN DE CONSULTAS DNI/RUC>
```

El token `FactilizaSettings__ConsultaToken` es independiente del token de
facturación.

El token no debe escribirse en ningún `appsettings*.json`. La URL de producción
está fijada en el código a `https://apife.factiliza.com/api/v1/` para evitar que
una configuración manipulada redirija la credencial a otro servidor.

En el primer despliegue de una base antigua, ejecuta el upgrade como un paso
separado, usando temporalmente una identidad de base de datos con permisos DDL:

```powershell
dotnet MiniMarket.Web.dll --apply-commercial-upgrade
```

El proceso aplica el esquema comercial/fiscal aditivo y termina sin iniciar el
servidor. Después utiliza para la aplicación una identidad limitada a las
operaciones ordinarias de lectura/escritura.

La primera emisión real se mostrará como `B002-00000001` o
`F002-00000001`. Una respuesta HTTP exitosa solo se considera aceptada cuando
Factiliza también devuelve respuesta SUNAT exitosa y CDR con código `0`.
En producción, emitir y solicitar una anulación fiscal exige volver a introducir
la contraseña vigente del administrador; los intentos fallidos participan en el
bloqueo normal de Identity.

## Contenido omitido de esta copia

Se dejaron fuera respaldos de base de datos, salidas de compilación/publicación,
claves locales de Data Protection, evidencias y scripts de QA, credenciales y
archivos de configuración de WebDeploy. El código fuente de las pruebas de
integración y la documentación operativa general sí se conservaron.
