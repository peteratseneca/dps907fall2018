### Packaging formats delivered by HRFormatterICT 

The rules below are based on what we typically do in a web app. For example, a "get all" will show a table of all items. Each item in the table will have a number of task-appropriate links (show details, edit, delete). And, there will often be a link to "add new". 

<br>

### Packaging format rules for returning a collection:

Data items
* All data items, using a shape that may or may not limit the number of properties returned  

Item controls (link relations)
* One link/control for EACH supported method on the item URI

Associated item controls
* None – deliver only the "base" rendering of the associated item 

Package controls 
* Self
* Add new (POST), if present / available 

<br>

### Packaging format rules for returning an item:

Data item
* One item's data, using a shape that may be fuller or more complete than above (or not)

Item controls
* None – the package has the controls

Associated item(s) controls
* None – again, deliver only the "base" rendering of the associated item(s) 

Package controls
* Self
* Collection
* One link/control for EACH supported method on the item URI

<br>
