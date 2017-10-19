const gulp = require('gulp');
const babel = require('gulp-babel');
const sourcemaps = require('gulp-sourcemaps');
const concat = require('gulp-concat');
const uglify = require('gulp-uglifyjs');
const browserify = require('gulp-browserify');
const cleanCSS = require('gulp-clean-css');
const sass = require('gulp-sass');
const watch = require('gulp-watch');

gulp.task('css', () => {
  return gulp.src(['src/components/**/*.scss', , 'src/site.scss'])
    .pipe(sass.sync())
    .pipe(cleanCSS({ compatibility: 'ie8' }))
    .pipe(concat('site.min.css'))
    .pipe(gulp.dest('../wwwroot/css'));
});

gulp.task('js', () =>
  gulp.src(['src/components/**/*.js', 'src/site.js'])
		.pipe(sourcemaps.init())
		.pipe(babel({
			presets: ['env']
    }))
    .pipe(uglify())
    .pipe(concat('site.min.js'))
    .pipe(browserify({
      insertGlobals: true
    }))
		.pipe(sourcemaps.write('.'))
    .pipe(gulp.dest('../wwwroot/js'))
);

gulp.task('templates', () => {
  return gulp.src(['src/index-head.html', 'src/index-body.html', 'src/components/**/*.html', 'src/index-scripts.html'])
    .pipe(concat('index.html'))
    .pipe(gulp.dest('../wwwroot'));
});

gulp.task('lib', () =>
  gulp.src([
    'node_modules/vue/dist/vue.js',
    'node_modules/vue-router/dist/vue-router.js',
    'node_modules/axios/dist/axios.js',
  ])
    .pipe(uglify())
    .pipe(concat('lib.min.js'))
    .pipe(gulp.dest('../wwwroot/js'))
);

gulp.task('watch', function () {
  gulp.watch('src/**/*.js', ['js']);
  gulp.watch('src/**/*.html', ['templates']);
  gulp.watch('src/**/*.scss', ['css']);
});

gulp.task('build', ['css', 'js', 'templates', 'lib']);
gulp.task('default', ['build', 'watch']);