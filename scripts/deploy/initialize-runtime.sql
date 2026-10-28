:On Error exit
SET NOCOUNT ON;
USE [master];
IF DB_ID(N'$(CRM_SQL_DATABASE)') IS NULL
    THROW 51000, 'Apply the explicit migrations before initializing the runtime login.', 1;
IF SUSER_ID(N'$(CRM_SQL_RUNTIME_USER)') IS NULL
BEGIN
    CREATE LOGIN [$(CRM_SQL_RUNTIME_USER)] WITH PASSWORD=N'$(CRM_SQL_RUNTIME_PASSWORD)',
        CHECK_POLICY=ON, CHECK_EXPIRATION=OFF;
END;
IF EXISTS (SELECT 1 FROM sys.server_role_members rm JOIN sys.server_principals p ON p.principal_id=rm.member_principal_id WHERE p.name=N'$(CRM_SQL_RUNTIME_USER)')
    THROW 51000, 'Runtime login already has a server role. Refusing elevated runtime credentials.', 1;
GO
USE [$(CRM_SQL_DATABASE)];
IF USER_ID(N'$(CRM_SQL_RUNTIME_USER)') IS NULL
    CREATE USER [$(CRM_SQL_RUNTIME_USER)] FOR LOGIN [$(CRM_SQL_RUNTIME_USER)] WITH DEFAULT_SCHEMA=[dbo];
ELSE
    ALTER USER [$(CRM_SQL_RUNTIME_USER)] WITH LOGIN=[$(CRM_SQL_RUNTIME_USER)];
IF DATABASE_PRINCIPAL_ID(N'crm_runtime_role') IS NULL
    CREATE ROLE [crm_runtime_role] AUTHORIZATION [dbo];
IF EXISTS (SELECT 1 FROM sys.database_role_members rm JOIN sys.database_principals u ON u.principal_id=rm.member_principal_id JOIN sys.database_principals r ON r.principal_id=rm.role_principal_id WHERE u.name=N'$(CRM_SQL_RUNTIME_USER)' AND r.name<>N'crm_runtime_role')
    THROW 51000, 'Runtime user already has a different database role. Review privileges before continuing.', 1;
GRANT CONNECT TO [$(CRM_SQL_RUNTIME_USER)];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[dbo] TO [crm_runtime_role];
DENY INSERT, UPDATE, DELETE ON OBJECT::[dbo].[__EFMigrationsHistory] TO [crm_runtime_role];
IF IS_ROLEMEMBER(N'crm_runtime_role',N'$(CRM_SQL_RUNTIME_USER)')<>1
    ALTER ROLE [crm_runtime_role] ADD MEMBER [$(CRM_SQL_RUNTIME_USER)];
PRINT 'Runtime SQL account mapped with dbo data permissions; no schema or server administration granted.';
