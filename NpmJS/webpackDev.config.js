const path = require("path");
const TerserPlugin = require("terser-webpack-plugin");

module.exports = {
    mode: "development",
    entry: "./src/index.js",
    output: {
        path: path.resolve(__dirname, "../wwwroot/js"),
        filename: "index.bundle.js"
    },
    optimization: {
        // We no not want to minimize our code.
        minimize: true,
        minimizer: [
            new TerserPlugin({
                parallel: true,
                terserOptions: {
                    // https://github.com/webpack-contrib/terser-webpack-plugin#terseroptions
                }
            })
        ]
    }
}