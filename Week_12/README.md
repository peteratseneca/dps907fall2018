## Week 12 code examples

Study the "ATTENTION" comment tokens in the code examples, as you also go through the class notes.  

<br>

### C# Manager.cs (with an HttpClient factory)

This is a partially-complete Manager class that you can learn from. 

Features:
* Correct and complete HttpClient factory
* Partially-complete method to get an access token from an IA Server
* Sample GET method for fetching an object or collection

<br>

### JavaScript manager.js

This is a partially-complete implementation of a Manager class in **JavaScript**, which will help you learn the coding design pattern for interacting with a web service from pure JavaScript. 

Features:
* Partially-complete method to get an access token from an IA Server
* Sample GET method for fetching an object or collection

<br>

### Angular Demo App

This is a demo application showing how to use the HttpClient service and Observables in modern (2017 and 2018) Angular.

#### Running the app

To run this application, you need Node.JS installed on your system. Then, from the command prompt, navigate to the folder that holds the app, and run the following commands:

```
npm install
npm start
```

This will transpile the TypeScript files, bundle everything using Webpack, and start the Node server. The app will be available at [http://localhost:3000/](http://localhost:3000/)

For background on the topic of Angular HTTP requests, please see Keith Dechant's blog posts:

https://www.metaltoad.com/blog/angular-2-http-observables-and-concurrent-data-loading

https://www.metaltoad.com/blog/angular-2-using-http-service-write-data-api

<br>

### iOS Web Api Get

An example of an **iOS app** that uses a web API.

The code is in many files (manager, starting view controller, and the table view controller. The super important parts are the `...+Chinook...` and `...+SICT...` extensions to the data model manager, and the new `WebApiRequest.swift` source code file that has a reusable class that does the web API interaction work. Enjoy.

The app was built with the Single View App project template.

<br>
