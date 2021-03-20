echo Copying header files to build destination
:: Error codes less than 8 should just be ignored
(robocopy ..\..\Headers .\Headers /E) ^& IF %ERRORLEVEL% LSS 8 SET ERRORLEVEL = 0