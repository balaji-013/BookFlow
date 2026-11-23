-- Optional hardening: least-privilege logins instead of 'sa' (SECURITY.md).
-- Run once as sa, then set ConnectionStrings__Default to use bookflow_app and the migrate job to use bookflow_migrator.
CREATE LOGIN bookflow_app WITH PASSWORD = 'Change_Me_App_Str0ng!';
CREATE LOGIN bookflow_migrator WITH PASSWORD = 'Change_Me_Mig_Str0ng!';
GO
USE BookFlow;
CREATE USER bookflow_app FOR LOGIN bookflow_app;
ALTER ROLE db_datareader ADD MEMBER bookflow_app;
ALTER ROLE db_datawriter ADD MEMBER bookflow_app;
CREATE USER bookflow_migrator FOR LOGIN bookflow_migrator;
ALTER ROLE db_ddladmin ADD MEMBER bookflow_migrator;
ALTER ROLE db_datareader ADD MEMBER bookflow_migrator;
ALTER ROLE db_datawriter ADD MEMBER bookflow_migrator;
GO
