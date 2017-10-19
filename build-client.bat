cd %1
cd client
echo Running Gulp Build in %1client
call npm install
call gulp build