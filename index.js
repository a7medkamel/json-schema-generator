var argv      = require('yargs').argv
  , util      = require('util')
  , edge      = require('edge')
  , path      = require('path')
  , ejs       = require('ejs')
  , fs        = require('fs-extra')
  , _         = require('underscore')
  , Promise   = require('bluebird')
  , beautify  = require('js-beautify').js_beautify
  , stringify = require('json-stringify-safe')
  ;

Promise.longStackTraces();

var src_path  = path.join(__dirname, './typeQuery.cs')
  , source    = fs.readFileSync(src_path, 'utf8')
  , tmpl_path = path.join(__dirname, './template/schema.ejs')
  , template  = fs.readFileSync(tmpl_path, 'utf8')
  , tmpl      = ejs.compile(template, { filename : tmpl_path })
  ;

var ls = edge.func({
    source      : source
  , references  : [ 'Microsoft.CSharp.dll', 'System.ComponentModel.DataAnnotations.dll']
});

function clrType(type) {
  switch(type) {
    case 'System.Object':
    return undefined;
  }

  return type;
}

function typeInfo(type, isEntity) {
  var ret = { };

  switch(type.TypeName) {
    case 'Int64':
    case 'Int32':
    case 'Int16':
    ret.jsonType = 'integer';
    break;
    case 'Float':
    case 'Double':
    ret.jsonType = 'number';
    break;
    case 'Boolean':
    ret.jsonType = 'boolean';
    break;
    case 'String':
    ret.jsonType = 'string';
    break;
    case 'DateTimeOffset':
    ret.jsonType = 'datetime';
    break;
    case 'Guid':
    ret.jsonType = 'uuid';
    break;
    default:
    if (_.size(type.Enum)) {
      ret.jsonType  = 'string';
    } else {
      // console.log(type.TypeName);
      ret.jsonType  = 'object';
      ret.$refType  = type.TypeName
    }
  }

  return ret;
}

function versionInfo(namespace, name) {
  var version = 1
    , match   = undefined
    ;

  if (m = /^Microsoft\.BingAds\.Api\.Model(\.V(\d+))?$/.exec(namespace)) {
    version = m[2]? Number(m[2]) : 1;
  }

  return {
      version : version
    , name_v  : version > 1? name + '_' + version : name
  };
}

function main(dll_path /*dll to inspect*/, output, terminal /*is running within the terminal on its own*/) {
  return Promise
          .promisify(ls)(dll_path)
          .then(function(schemas){
            return Promise
                    .promisify(fs.remove)(output)
                    .then(function(){
                      return schemas;
                    });
          })
          .then(function(schemas){
            var arr = _
                        .chain(schemas)
                        .filter(function(i){ return !_.isEmpty(i.Properties); })
                        .filter(function(i){ return i.BaseType !== 'System.Attribute'; })
                        .value()
                        ;

            console.log('Filtered %s Classes to %s Schemas', _.size(schemas), _.size(arr));

            return arr;
          })
          .then(function(schemas){
            var arr = [];

            schemas.forEach(function(schema){
              var properties = _.map(schema.Properties, function(prop){
                var isKey   = !!_.find(prop.Attributes, function(attr){ return attr.Name === 'KeyAttribute'; })
                  , info    = typeInfo(prop, isKey)
                  , info_v  = versionInfo(prop.TypeNamespace, prop.TypeName) 
                  ;

                return {
                    name        : prop.Name
                  , type        : prop.IsEnumerable? 'array' : info.jsonType
                  , version     : info_v.version
                  , name_v      : info_v.name_v
                  , itemType    : prop.IsEnumerable? info.jsonType : undefined
                  , $refType    : info.$refType
                  , 'enum'      : prop.Enum      
                  // , $ref        : info.$ref
                  // , clrType     : prop.TypeName
                  , key         : isKey
                  , nullable    : prop.IsNullable
                  , enumerable  : prop.IsEnumerable
                };
              });

              var key     = _.find(properties, function(prop){ return prop.key; })
                , info_v  = versionInfo(schema.Namespace, schema.Name) 
                ;

              var model = {
                  name          : schema.Name
                , version       : info_v.version
                , name_v        : info_v.name_v
                , key           : key? key.name : undefined
                , clrType       : clrType(schema.FullName)          
                , clrTypeBase   : clrType(schema.BaseType)
                , properties    : properties
                // , deps          : deps
              };

              arr.push({
                  __original  : schema
                , schema      : model
                // , text        : beautified
              });
            });

            return arr;
          })
          .then(function(arr){
            var lookup = _.indexBy(arr, function(i){ return i.schema.name; });

            var tree = {
                root      : {}
              , deps      : {}
              , orphans   : {}
              , missing   : {}
              , 'enum'    : {}
              , __all     : arr
            };

            _.each(arr, function(i){
              var m         = i.schema
                , isEntity  = !!m.key
                ;

              if (isEntity) {
                tree.root[i.schema.name_v] = i;
                i.inTree = true;
              }
              
              _.each(m.properties, function(p){
                var $refType = p.$refType

                if ($refType) {
                  var p_item = lookup[$refType];
                  if (!p_item) {
                    var info = { owner : i, property : p };
                    if (_.size(tree.enum)) {
                      tree.enum[$refType] = info;
                    } else {
                      tree.missing[$refType] = info;
                    }
                    return;
                  }

                  tree.deps[p_item.schema.name_v] = p_item;
                  p.schema = p_item.schema;
                  p_item.inTree = true;
                }
              });
            });

            // detect orphans
            _.each(arr, function(i){
              if (!i.inTree) {
                tree.orphans[i.schema.name_v] = i;
              }
            });

            return tree;          
          })
          .tap(function(tree){
            // generate $ref and category

            _.each(tree.__all, function(i){
              var category  = 'entity'
                , name      = i.schema.name_v
                ;

              if (tree.root[name]) { category = 'entity'; }
              else if (tree.deps[name]) { category = 'deps'; }
              else if (tree.orphans[name]) { category = 'orphans'; }
              else if (tree.missing[name]) { category = 'missing'; }

              i.schema.$category = category;
              i.schema.$ref = (category === 'entity')
                                  ? 'campaign/' + i.schema.name_v
                                  : 'campaign/' + category + '/' + i.schema.name_v;
            });
          })
          .tap(function(tree){
            // generate output here

            _.each(tree.__all, function(i){
              var out = tmpl(i.schema);

              i.text = beautify(out, { 
                  indent_size: 2
                , preserve_newlines : false
              });
            });

            if (_.size(tree.missing)) {
              console.error('Missing: ', _.map(tree.missing, function(i, k){
                return {
                    type      : k
                  , property  : i.property.name
                  , owner     : { name : i.owner.schema.name_v, category : i.owner.schema.$category }
                };
              }));
            }
          })
          .tap(function(tree){
            if (!terminal) {
              var writers = _.map(tree.__all, function(item){
                var root    = (item.schema.$category === 'entity')? output : path.join(output, item.schema.$category)
                  , to      = path.join(root,  item.schema.name_v + '.js')
                  ;

                return Promise.promisify(fs.outputFile)(to, item.text, 'utf8');
              });
              

              return Promise
                      .all(writers)
                      .then(function(){
                        var txt = stringify(_.pluck(tree.__all, '__original'), null, 2)
                          , to  = path.join(output, '../tree.js')
                          ;

                        return Promise.promisify(fs.outputFile)(to, txt, 'utf8');
                      })
                      .then(function(){
                        var tmpl_path = path.join(__dirname, './template/json.ejs')
                          , template  = fs.readFileSync(tmpl_path, 'utf8')
                          , tmpl      = ejs.compile(template, { filename : tmpl_path })
                          ;

                        var resources = _.keys(tree.root)
                          , data      = {
                              resources : resources.sort()
                          }
                          , txt       = tmpl({ json : stringify(data, null, 2) })
                          , to        = path.join(output, '$metadata.js')
                          ;

                        return Promise.promisify(fs.outputFile)(to, txt, 'utf8');
                      })
                      ;              
            }
          })
          .catch(function(err){
            terminal || console.error(err.stack);
            throw err;
          });
}

if (require.main === module) {
  if (!argv.i) {
    argv.i = '..\\..\\..\\..\\..\\Campaign\\MT\\Source\\BingAds.Api\\BingAds.Api.Model\\objd\\amd64\\BingAds.Api.Model.dll';
    console.log('using default dll path', argv.i)
  }

  main(argv.i, argv.o, true /*terminal*/)
  .then(function(text){
    console.log(text);
  })
  .catch(function(err){
    console.error(err.stack);
  });
}

module.exports = {
  main : main
};