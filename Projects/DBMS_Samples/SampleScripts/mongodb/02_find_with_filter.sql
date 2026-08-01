db.products.find({ "unit_price": { "$gt": 300000 } }).limit(10)
